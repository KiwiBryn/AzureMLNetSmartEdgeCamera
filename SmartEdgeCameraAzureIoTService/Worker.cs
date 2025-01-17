//---------------------------------------------------------------------------------
// Copyright (c) April 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
namespace devMobile.IoT.MachineLearning.SmartEdgeCameraAzureIoTService
{
	using System;
	using System.Collections.Generic;
#if CAMERA_RASPBERRY_PI
	using System.Diagnostics;
#endif
	using System.Globalization;
#if AZURE_STORAGE_IMAGE_UPLOAD || CAMERA_SECURITY
   using System.IO;
#endif
	using System.Linq;
#if CAMERA_SECURITY
	using System.Net;
	using System.Net.Http;
	using System.Text;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
	using System.Security.Cryptography;
#endif
#if AZURE_DEVICE_PROPERTIES
	using System.Reflection;
#endif
	using System.Threading;
	using System.Threading.Tasks;

#if AZURE_STORAGE_IMAGE_UPLOAD
	using Azure;
	using Azure.Storage.Blobs.Specialized;
	using Azure.Storage.Blobs.Models;
#endif

	using Microsoft.Azure.Devices.Client;
#if AZURE_STORAGE_IMAGE_UPLOAD
	using Microsoft.Azure.Devices.Client.Transport;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
	using Microsoft.Azure.Devices.Provisioning.Client;
	using Microsoft.Azure.Devices.Provisioning.Client.Transport;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION || AZURE_DEVICE_PROPERTIES
	using Microsoft.Azure.Devices.Shared;
#endif
	using Microsoft.Extensions.Hosting;
	using Microsoft.Extensions.Logging;
	using Microsoft.Extensions.Options;

	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

   using SixLabors.Fonts;
   using SixLabors.ImageSharp;
   using SixLabors.ImageSharp.Drawing.Processing;
   using SixLabors.ImageSharp.PixelFormats;
   using SixLabors.ImageSharp.Processing;

   using Yolov5Net.Scorer;
   using Yolov5Net.Scorer.Models;

   // Compile time options
   // CAMERA_SECURITY
   //		or
   // CAMERA_RASPBERRY_PI
   //
   // AZURE_STORAGE_IMAGE_UPLOAD
   //
   // AZURE_IOT_HUB_CONNECTION
   //		or
   //	AZURE_IOT_HUB_DPS_CONNECTION
   //
   // AZURE_DEVICE_PROPERTIES

	public class Worker : BackgroundService
	{
		private readonly ILogger<Worker> _logger;
		private readonly ApplicationSettings _applicationSettings;
#if CAMERA_SECURITY
		private readonly SecurityCameraSettings _securityCameraSettings;
		private HttpClient _httpClient;
#endif
#if CAMERA_RASPBERRY_PI
		private readonly RaspberryPICameraSettings _raspberryPICameraSettings;
#endif
#if AZURE_STORAGE_IMAGE_UPLOAD
		private readonly AzureStorageSettings _azureStorageSettings;
#endif
#if AZURE_IOT_HUB_CONNECTION
		private readonly AzureIoTHubSettings _azureIoTHubSettings;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
		private readonly AzureIoTHubDpsSettings _azureIoTHubDpsSettings;
#endif
		private static YoloScorer<YoloCocoP5Model> _scorer = null;
		private bool _cameraBusy = false;
		private static DeviceClient _deviceClient;
		private Timer _ImageUpdatetimer;

		public Worker(ILogger<Worker> logger
			,IOptions<ApplicationSettings> applicationSettings
#if CAMERA_SECURITY
			,IOptions<SecurityCameraSettings> securityCameraSettings
#endif
#if CAMERA_RASPBERRY_PI
			,IOptions<RaspberryPICameraSettings> raspberryPICameraSettings
#endif
#if AZURE_STORAGE_IMAGE_UPLOAD
			,IOptions<AzureStorageSettings> azureStorageSettings
#endif
#if AZURE_IOT_HUB_CONNECTION
			,IOptions<AzureIoTHubSettings> azureIoTHubSettings
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
			,IOptions<AzureIoTHubDpsSettings> azureIoTHubDpsSettings
#endif
			)
		{
			_logger = logger;

			_applicationSettings = applicationSettings.Value;
#if CAMERA_SECURITY
			_securityCameraSettings = securityCameraSettings.Value;
#endif
#if CAMERA_RASPBERRY_PI
			_raspberryPICameraSettings = raspberryPICameraSettings.Value;
#endif
#if AZURE_STORAGE_IMAGE_UPLOAD
			_azureStorageSettings = azureStorageSettings.Value;
#endif
#if AZURE_IOT_HUB_CONNECTION
			_azureIoTHubSettings = azureIoTHubSettings.Value;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
			_azureIoTHubDpsSettings = azureIoTHubDpsSettings.Value;
#endif
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("Azure IoT Smart Edge Camera Service starting");

			try
			{
#if CAMERA_SECURITY
				NetworkCredential networkCredential = new NetworkCredential(_securityCameraSettings.CameraUserName, _securityCameraSettings.CameraUserPassword);

				_httpClient = new HttpClient(new HttpClientHandler { PreAuthenticate = true, Credentials = networkCredential });
#endif

#if AZURE_IOT_HUB_CONNECTION
				_deviceClient = await AzureIoTHubConnection();
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
				_deviceClient = await AzureIoTHubDpsConnection();
#endif

#if AZURE_DEVICE_PROPERTIES
				_logger.LogTrace("ReportedPropeties upload start");

				TwinCollection reportedProperties = new TwinCollection();

				reportedProperties["OSVersion"] = Environment.OSVersion.VersionString;
				reportedProperties["MachineName"] = Environment.MachineName;
				reportedProperties["ApplicationVersion"] = Assembly.GetAssembly(typeof(Program)).GetName().Version;
				reportedProperties["ImageTimerDue"] = _applicationSettings.ImageTimerDue;
				reportedProperties["ImageTimerPeriod"] = _applicationSettings.ImageTimerPeriod;
				reportedProperties["YoloV5ModelPath"] = _applicationSettings.YoloV5ModelPath;

				reportedProperties["PredictionScoreThreshold"] = _applicationSettings.PredictionScoreThreshold;
				reportedProperties["PredictionLabelsOfInterest"] = _applicationSettings.PredictionLabelsOfInterest;
				reportedProperties["PredictionLabelsMinimum"] = _applicationSettings.PredictionLabelsMinimum;

				await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties, stoppingToken);

				_logger.LogTrace("ReportedPropeties upload done");
#endif

				_logger.LogTrace("YoloV5 model setup start");
				_scorer = new YoloScorer<YoloCocoP5Model>(_applicationSettings.YoloV5ModelPath);
				_logger.LogTrace("YoloV5 model setup done");

				_ImageUpdatetimer = new Timer(ImageUpdateTimerCallback, null, _applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

				await _deviceClient.SetMethodHandlerAsync("ImageTimerStart", ImageTimerStartHandler, null);
				await _deviceClient.SetMethodHandlerAsync("ImageTimerStop", ImageTimerStopHandler, null);
				await _deviceClient.SetMethodDefaultHandlerAsync(DefaultHandler, null);

#if AZURE_DEVICE_PROPERTIES
				await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChangedAsync, null);
#endif
				try
				{
					await Task.Delay(Timeout.Infinite, stoppingToken);
				}
				catch (TaskCanceledException)
				{
					_logger.LogInformation("Application shutown requested");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Application startup failure");
			}
			finally
			{
				_deviceClient?.Dispose();
			}

			_logger.LogInformation("Azure IoT Smart Edge Camera Service shutdown");
		}

		private async Task<MethodResponse> ImageTimerStartHandler(MethodRequest methodRequest, object userContext)
		{
			_logger.LogInformation("Direct Method Image Timer Start Due:{0} Period:{1}", _applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

			_ImageUpdatetimer.Change(_applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

			return new MethodResponse((short)HttpStatusCode.OK);
		}

		private async Task<MethodResponse> ImageTimerStopHandler(MethodRequest methodRequest, object userContext)
		{
			_logger.LogInformation("Direct method Image Timer Stop");

			_ImageUpdatetimer.Change(Timeout.Infinite, Timeout.Infinite);

			return new MethodResponse((short)HttpStatusCode.OK);
		}

		private async Task<MethodResponse> DefaultHandler(MethodRequest methodRequest, object userContext)
		{
			_logger.LogInformation("Direct Method default handler Name:{0}", methodRequest.Name);

			return new MethodResponse((short)HttpStatusCode.NotImplemented);
		}

#if AZURE_DEVICE_PROPERTIES
		private async Task OnDesiredPropertyChangedAsync(TwinCollection desiredProperties, object userContext)
		{
			TwinCollection reportedProperties = new TwinCollection();

			_logger.LogInformation("OnDesiredPropertyChanged handler");

			// NB- This approach does not save the ImageTimerDue or ImageTimerPeriod, a stop/start with return to appsettings.json configuration values. If only
			// one parameter specified other is default from appsettings.json. If timer settings changed I think they won't take
			// effect until next time Timer fires.

			try
			{
				// Check to see if either of ImageTimerDue or ImageTimerPeriod has changed
				if (!desiredProperties.Contains("ImageTimerDue") && !desiredProperties.Contains("ImageTimerPeriod"))
				{
					_logger.LogInformation("OnDesiredPropertyChanged neither ImageTimerDue or ImageTimerPeriod present");
					return;
				}

				TimeSpan imageTimerDue = _applicationSettings.ImageTimerDue;

				// Check that format of ImageTimerDue valid if present
				if (desiredProperties.Contains("ImageTimerDue"))
				{
					if (TimeSpan.TryParse(desiredProperties["ImageTimerDue"].Value, out imageTimerDue))
					{
						reportedProperties["ImageTimerDue"] = imageTimerDue;
					}
					else
					{
						_logger.LogInformation("OnDesiredPropertyChanged ImageTimerDue invalid");
						return;
					}
				}

				TimeSpan imageTimerPeriod = _applicationSettings.ImageTimerPeriod;

				// Check that format of ImageTimerPeriod valid if present
				if (desiredProperties.Contains("ImageTimerPeriod"))
				{
					if (TimeSpan.TryParse(desiredProperties["ImageTimerPeriod"].Value, out imageTimerPeriod))
					{
						reportedProperties["ImageTimerPeriod"] = imageTimerPeriod;
					}
					else
					{
						_logger.LogInformation("OnDesiredPropertyChanged ImageTimerPeriod invalid");
						return;
					}
				}

				_logger.LogInformation("Desired Due:{0} Period:{1}", imageTimerDue, imageTimerPeriod);

				if (!_ImageUpdatetimer.Change(imageTimerDue, imageTimerPeriod))
				{
					_logger.LogInformation("Desired Due:{0} Period:{1} failed", imageTimerDue, imageTimerPeriod);
				}

				await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "OnDesiredPropertyChangedAsync handler failed");
			}
		}
#endif

		private async void ImageUpdateTimerCallback(object state)
		{
			DateTime requestAtUtc = DateTime.UtcNow;

			// Just incase - stop code being called while photo already in progress
			if (_cameraBusy)
			{
				return;
			}
			_cameraBusy = true;

			_logger.LogInformation("Image processing start");

			try
			{
#if CAMERA_RASPBERRY_PI
				RaspberryPIImageCapture();
#endif
#if CAMERA_SECURITY
				await SecurityCameraImageCaptureAsync();
#endif
				List<YoloPrediction> predictions;


            using var image = await Image.LoadAsync<Rgba32>(_applicationSettings.ImageCameraFilepath);
            {
               using var scorer = new YoloScorer<YoloCocoP5Model>(_applicationSettings.YoloV5ModelPath);
               {
                  predictions = scorer.Predict(image);

                  if (_logger.IsEnabled(LogLevel.Trace))
                  {
                     _logger.LogTrace("Predictions {0}", predictions.Select(p => new { p.Label.Name, p.Score }));
                  }

                  var font = new Font(new FontCollection().Add(_applicationSettings.ImageMarkUpFontPath), _applicationSettings.ImageMarkUpFontSize);

                  foreach (var prediction in predictions)
                  {
                     double score = Math.Round(prediction.Score, 2);

                     var (x, y) = (prediction.Rectangle.Left - 3, prediction.Rectangle.Top - 23);

                     image.Mutate(a => a.DrawPolygon(Pens.Solid(prediction.Label.Color, 1),
                         new PointF(prediction.Rectangle.Left, prediction.Rectangle.Top),
                         new PointF(prediction.Rectangle.Right, prediction.Rectangle.Top),
                         new PointF(prediction.Rectangle.Right, prediction.Rectangle.Bottom),
                         new PointF(prediction.Rectangle.Left, prediction.Rectangle.Bottom)
                     ));

                     image.Mutate(a => a.DrawText($"{prediction.Label.Name} ({score})",
                         font, prediction.Label.Color, new PointF(x, y)));
                  }

                  image.Save(_applicationSettings.ImageMarkedUpFilepath);
               }
            }

            var predictionsValid = predictions.Where(p => p.Score >= _applicationSettings.PredictionScoreThreshold).Select(p => p.Label.Name);

				// Count up the number of each class detected in the image
				var predictionsTally = predictionsValid.GroupBy(p => p)
						.Select(p => new
						{
							Label = p.Key,
							Count = p.Count()
						});

				if (_logger.IsEnabled(LogLevel.Information))
				{
					_logger.LogInformation("Predictions tally before {0}", predictionsTally.ToList());
				}

				// Add in any missing counts the cloudy side is expecting
				if (_applicationSettings.PredictionLabelsMinimum != null)
				{
					foreach( String label in _applicationSettings.PredictionLabelsMinimum)
					{
						if (!predictionsTally.Any(c=>c.Label == label ))
						{
							predictionsTally = predictionsTally.Append(new {Label = label, Count = 0 });
						}
					}
				}

				if (_logger.IsEnabled(LogLevel.Information))
				{
					_logger.LogInformation("Predictions tally after {0}", predictionsTally.ToList());
				}

				if ((_applicationSettings.PredictionLabelsOfInterest == null) || (predictionsValid.Select(c => c).Intersect(_applicationSettings.PredictionLabelsOfInterest, StringComparer.OrdinalIgnoreCase).Any()))
				{
					JObject telemetryDataPoint = new JObject();

					foreach (var predictionTally in predictionsTally)
					{
						telemetryDataPoint.Add(predictionTally.Label, predictionTally.Count);
					}

					using (Message message = new Message(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(telemetryDataPoint))))
					{
						message.Properties.Add("iothub-creation-time-utc", requestAtUtc.ToString("s", CultureInfo.InvariantCulture));

						await _deviceClient.SendEventAsync(message);
					}

#if AZURE_STORAGE_IMAGE_UPLOAD
					if (_applicationSettings.ImageCameraUpload)
					{
						_logger.LogTrace("Image camera upload start");

						await UploadImage(predictions, _applicationSettings.ImageCameraFilepath, string.Format(_azureStorageSettings.ImageCameraFilenameFormat, requestAtUtc));

						_logger.LogTrace("Image camera upload done");
					}

					if (_applicationSettings.ImageMarkedupUpload)
					{
						_logger.LogTrace("Image marked-up upload start");

						await UploadImage(predictions, _applicationSettings.ImageMarkedUpFilepath, string.Format(_azureStorageSettings.ImageMarkedUpFilenameFormat, requestAtUtc));

						_logger.LogTrace("Image marked-up upload done");
					}
#endif
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Camera image download, post processing, or telemetry failed");
			}
			finally
			{
				_cameraBusy = false;
			}

			TimeSpan duration = DateTime.UtcNow - requestAtUtc;

			_logger.LogInformation("Image processing done {0:f2} sec", duration.TotalSeconds);
		}

#if CAMERA_SECURITY
		private async Task SecurityCameraImageCaptureAsync()
		{
			_logger.LogTrace("Security Camera Image download start");

			using (Stream cameraStream = await _httpClient.GetStreamAsync(_securityCameraSettings.CameraUrl))
			using (Stream fileStream = File.Create(_applicationSettings.ImageCameraFilepath))
			{
				await cameraStream.CopyToAsync(fileStream);
			}

			_logger.LogTrace("Security Camera Image download done");
		}
#endif

#if CAMERA_RASPBERRY_PI
		private void RaspberryPIImageCapture()
		{
			_logger.LogTrace("Raspberry PI Image capture start");

			using (Process process = new Process())
			{
				process.StartInfo.FileName = @"libcamera-jpeg";
				process.StartInfo.Arguments = $"-o {_applicationSettings.ImageCameraFilepath} --nopreview -t1 --rotation {_raspberryPICameraSettings.Rotation}";
				process.StartInfo.RedirectStandardError = true;

				process.Start();

				if (!process.WaitForExit(_raspberryPICameraSettings.ProcessWaitForExit) || (process.ExitCode != 0))
				{
					_logger.LogError("Raspberry PI Image capture failure ExitCode:{0}", process.ExitCode);
				}
			}

			_logger.LogTrace("Raspberry PI Image capture done");
		}
#endif

#if AZURE_IOT_HUB_CONNECTION
		private async Task<DeviceClient> AzureIoTHubConnection()
		{
			_logger.LogTrace("Azure IoT Hub connection start");

			DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(_azureIoTHubSettings.ConnectionString, _applicationSettings.DeviceId);

			await deviceClient.OpenAsync();

			_logger.LogTrace("Azure IoT Hub connection done");

			return deviceClient;
		}
#endif

#if AZURE_IOT_HUB_DPS_CONNECTION
		private async Task<DeviceClient> AzureIoTHubDpsConnection()
		{
			string deviceKey;
			DeviceClient deviceClient;

			_logger.LogTrace("Azure IoT Hub DPS connection start");

			using (var hmac = new HMACSHA256(Convert.FromBase64String(_azureIoTHubDpsSettings.GroupEnrollmentKey)))
			{
				deviceKey = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(_applicationSettings.DeviceId)));
			}

			using (var securityProvider = new SecurityProviderSymmetricKey(_applicationSettings.DeviceId, deviceKey, null))
			{
				using (var transport = new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly))
				{
					ProvisioningDeviceClient provClient = ProvisioningDeviceClient.Create(_azureIoTHubDpsSettings.GlobalDeviceEndpoint, _azureIoTHubDpsSettings.IDScope, securityProvider, transport);

					DeviceRegistrationResult result = await provClient.RegisterAsync();

					_logger.LogInformation("Hub:{0} DeviceID:{1} RegistrationID:{2} Status:{3}", result.AssignedHub, result.DeviceId, result.RegistrationId, result.Status);
					if (result.Status != ProvisioningRegistrationStatusType.Assigned)
					{
						_logger.LogTrace("DeviceID:{0} {1} already assigned", result.DeviceId, result.Status);
					}

					IAuthenticationMethod authentication = new DeviceAuthenticationWithRegistrySymmetricKey(result.DeviceId, (securityProvider as SecurityProviderSymmetricKey).GetPrimaryKey());

					deviceClient = DeviceClient.Create(result.AssignedHub, authentication, TransportType.Amqp_Tcp_Only);
				}
			}

			await deviceClient.OpenAsync();

			_logger.LogTrace("Azure IoT Hub DPS connection start");

			return deviceClient;
		}
#endif

#if AZURE_STORAGE_IMAGE_UPLOAD
		public static async Task UploadImage(List<YoloPrediction> predictions, string filepath, string blobpath)
		{
			var fileUploadSasUriRequest = new FileUploadSasUriRequest()
			{
				BlobName = blobpath 
			};

			FileUploadSasUriResponse sasUri = await _deviceClient.GetFileUploadSasUriAsync(fileUploadSasUriRequest);

			var blockBlobClient = new BlockBlobClient(sasUri.GetBlobUri());

         BlobUploadOptions blobUploadOptions = new BlobUploadOptions()
         {
            Metadata = new Dictionary<string, string>()
            //Tags = new Dictionary<string, string>()
         };

         foreach (var prediction in predictions)
         {
            blobUploadOptions.Metadata.Add(prediction.Label.Name, predictions.Count.ToString());
            //blobUploadOptions.Tags.Add(prediction.Label.Name, predictions.Count.ToString());
         }

         var fileUploadCompletionNotification = new FileUploadCompletionNotification()
			{
				// Mandatory. Must be the same value as the correlation id returned in the sas uri response
				CorrelationId = sasUri.CorrelationId,

				IsSuccess = true
			};

			try
			{
				using (FileStream fileStream = File.OpenRead(filepath))
				{
					Response<BlobContentInfo> response = await blockBlobClient.UploadAsync(fileStream, blobUploadOptions);

               fileUploadCompletionNotification.StatusCode = response.GetRawResponse().Status;

					if (fileUploadCompletionNotification.StatusCode != ((int)HttpStatusCode.Created))
					{
						fileUploadCompletionNotification.IsSuccess = false;

						fileUploadCompletionNotification.StatusDescription = response.GetRawResponse().ReasonPhrase;
					}
				}
			}
			catch (RequestFailedException ex)
			{
				fileUploadCompletionNotification.StatusCode = ex.Status;

				fileUploadCompletionNotification.IsSuccess = false;

				fileUploadCompletionNotification.StatusDescription = ex.Message;

				throw;
			}
			finally
			{
				await _deviceClient.CompleteFileUploadAsync(fileUploadCompletionNotification);
			}
		}
#endif
   }
}