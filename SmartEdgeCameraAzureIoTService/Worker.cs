//---------------------------------------------------------------------------------
// Copyright (c) April 2022, devMobile Software
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//---------------------------------------------------------------------------------
namespace devMobile.IoT.MachineLearning.SmartEdgeCameraAzureIoTService
{
	using System;
	using System.Collections.Generic;
	using System.Drawing;
#if CAMERA_RASPBERRY_PI
	using System.Diagnostics;
#endif
	using System.Globalization;
#if AZURE_STORAGE_IMAGE_UPLOAD
	using System.IO;
#endif
	using System.Linq;
#if CAMERA_SECURITY
	using System.Net;
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

		public Worker(ILogger<Worker> logger,
			IOptions<ApplicationSettings> applicationSettings,
#if CAMERA_SECURITY
			IOptions<SecurityCameraSettings> securityCameraSettings,
#endif
#if CAMERA_RASPBERRY_PI
			IOptions<RaspberryPICameraSettings> raspberryPICameraSettings,
#endif
#if AZURE_STORAGE_IMAGE_UPLOAD
			IOptions<AzureStorageSettings> azureStorageSettings,
#endif
#if AZURE_IOT_HUB_CONNECTION
			IOptions<AzureIoTHubSettings> azureIoTHubSettings
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
			IOptions<AzureIoTHubDpsSettings> azureIoTHubDpsSettings
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
				if (_applicationSettings.PredictionLabelsOfInterest != null)
				{
					reportedProperties["PredictionLabelsOfInterest"] = _applicationSettings.PredictionLabelsOfInterest.ToList();
				}
				else
				{
					reportedProperties["PredictionLabelsOfInterest"] = "";
				}
				if (_applicationSettings.PredictionLabelsMinimum != null)
				{
					reportedProperties["PredictionLabelsMinimum"] = _applicationSettings.PredictionLabelsMinimum.ToList();
				}
				else
				{
					reportedProperties["PredictionLabelsMinimum"] = "";

				}
				reportedProperties["PredictionScoreThreshold"] = _applicationSettings.PredictionScoreThreshold;

				await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties, stoppingToken);

				_logger.LogTrace("ReportedPropeties upload done");
#endif

				_logger.LogTrace("YoloV5 model setup start");
				_scorer = new YoloScorer<YoloCocoP5Model>(_applicationSettings.YoloV5ModelPath);
				_logger.LogTrace("YoloV5 model setup done");

				_ImageUpdatetimer = new Timer(ImageUpdateTimerCallback, null, _applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

				await _deviceClient.SetMethodHandlerAsync("ImageTimerStart", ImageTimerStartHandler, null);
				await _deviceClient.SetMethodHandlerAsync("ImageTimerStop", ImageTimerStopHandler, null);

				await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChangedAsync, null);

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
			_logger.LogInformation("ImageUpdatetimer Start Due:{0} Period:{1}", _applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

			_ImageUpdatetimer.Change(_applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

			return new MethodResponse(200);
		}

		private async Task<MethodResponse> ImageTimerStopHandler(MethodRequest methodRequest, object userContext)
		{
			_logger.LogInformation("ImageUpdatetimer Stop");

			_ImageUpdatetimer.Change(Timeout.Infinite, Timeout.Infinite);

			return new MethodResponse(200);
		}

		private async Task OnDesiredPropertyChangedAsync(TwinCollection desiredProperties, object userContext)
		{
			_logger.LogInformation("OnDesiredPropertyChanged timer");

			if (!desiredProperties.Contains("ImageTimerDue") || !desiredProperties.Contains("ImageTimerPeriod"))
			{
				_logger.LogInformation("OnDesiredPropertyChanged ImageTimerDue or ImageTimerPeriod missing");
				return;
			}

			if (!TimeSpan.TryParse(desiredProperties["ImageTimerDue"].Value, out TimeSpan imageTimerDue))
			{
				_logger.LogInformation("OnDesiredPropertyChanged ImageTimerDue invalid");
				return;
			}

			if (!TimeSpan.TryParse(desiredProperties["ImageTimerPeriod"].Value, out TimeSpan imageTimerPeriod))
			{
				_logger.LogInformation("OnDesiredPropertyChanged ImageTimerPeriod invalid");
				return;
			}

			if (_ImageUpdatetimer.Change(imageTimerDue, imageTimerPeriod))
			{
				_logger.LogInformation("OnDesiredPropertyChanged Timer.Change({0},{1}) success", imageTimerDue, imageTimerPeriod);
			}
			else
			{
				_logger.LogInformation("OnDesiredPropertyChanged Timer.Change({0},{1}) failure", imageTimerDue, imageTimerPeriod);
			}
		}

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
				SecurityCameraImageCapture();
#endif
				List<YoloPrediction> predictions;

				using (Image image = Image.FromFile(_applicationSettings.ImageCameraFilepath))
				{
					_logger.LogTrace("Prediction start");
					predictions = _scorer.Predict(image);
					_logger.LogTrace("Prediction done");

					OutputImageMarkup(image, predictions, _applicationSettings.ImageMarkedUpFilepath);
				}

				if (_logger.IsEnabled(LogLevel.Trace))
				{
					_logger.LogTrace("Predictions {0}", predictions.Select(p => new { p.Label.Name, p.Score }));
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
		private void SecurityCameraImageCapture()
		{
			_logger.LogTrace("Security Camera Image download start");

			NetworkCredential networkCredential = new NetworkCredential()
			{
				UserName = _securityCameraSettings.CameraUserName,
				Password = _securityCameraSettings.CameraUserPassword,
			};

			using (WebClient client = new WebClient())
			{
				client.Credentials = networkCredential;

				client.DownloadFile(_securityCameraSettings.CameraUrl, _applicationSettings.ImageCameraFilepath);
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

		public void OutputImageMarkup(Image image, List<YoloPrediction> predictions, string filepath)
		{
			_logger.LogTrace("Image markup start");

			using (Graphics graphics = Graphics.FromImage(image))
			{

				foreach (var prediction in predictions) // iterate predictions to draw results
				{
					double score = Math.Round(prediction.Score, 2);

					graphics.DrawRectangles(new Pen(prediction.Label.Color, 1), new[] { prediction.Rectangle });

					var (x, y) = (prediction.Rectangle.X - 3, prediction.Rectangle.Y - 23);

					graphics.DrawString($"{prediction.Label.Name} ({score})", new Font("Consolas", 16, GraphicsUnit.Pixel), new SolidBrush(prediction.Label.Color), new PointF(x, y));
				}

				image.Save(filepath);
			}

			_logger.LogTrace("Image markup done");
		}

		public static async Task UploadImage(List<YoloPrediction> predictions, string filepath, string blobpath)
		{
			var fileUploadSasUriRequest = new FileUploadSasUriRequest()
			{
				BlobName = blobpath 
			};

			FileUploadSasUriResponse sasUri = await _deviceClient.GetFileUploadSasUriAsync(fileUploadSasUriRequest);

			var blockBlobClient = new BlockBlobClient(sasUri.GetBlobUri());

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
					Response<BlobContentInfo> response = await blockBlobClient.UploadAsync(fileStream); //, blobUploadOptions);

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
	}
}