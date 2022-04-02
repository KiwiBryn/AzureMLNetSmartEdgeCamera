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
	using System.Linq;
#if CAMERA_SECURITY
	using System.Net;
	using System.Text;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
	using System.Security.Cryptography;
#endif
	using System.Threading;
	using System.Threading.Tasks;

	using Microsoft.Azure.Devices.Client;
#if AZURE_IOT_HUB_DPS_CONNECTION
	using Microsoft.Azure.Devices.Shared;
	using Microsoft.Azure.Devices.Provisioning.Client;
	using Microsoft.Azure.Devices.Provisioning.Client.Transport;
#endif
	using Microsoft.Extensions.Hosting;
	using Microsoft.Extensions.Logging;
	using Microsoft.Extensions.Options;

	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	using Yolov5Net.Scorer;
	using Yolov5Net.Scorer.Models;

	// Compile time options
	// AZURE_IOT_HUB_CONNECTION
	//		or
	//	AZURE_IOT_HUB_DPS_CONNECTION

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
#if AZURE_IOT_HUB_CONNECTION
		private readonly AzureIoTHubSettings _azureIoTHubSettings;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
		private readonly AzureIoTHubDpsSettings _azureIoTHubDpsSettings;
#endif
		private static YoloScorer<YoloCocoP5Model> _scorer = null;
		private bool _cameraBusy = false;
		private static DeviceClient _deviceClient;

		public Worker(ILogger<Worker> logger,
			IOptions<ApplicationSettings> applicationSettings,
#if CAMERA_SECURITY
			IOptions<SecurityCameraSettings> securityCameraSettings,
#endif
#if CAMERA_RASPBERRY_PI
			IOptions<RaspberryPICameraSettings> raspberryPICameraSettings,
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

				_logger.LogTrace("YoloV5 model setup start");
				_scorer = new YoloScorer<YoloCocoP5Model>(_applicationSettings.YoloV5ModelPath);
				_logger.LogTrace("YoloV5 model setup done");

				Timer imageUpdatetimer = new Timer(ImageUpdateTimerCallback, null, _applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

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
				}

				if (_logger.IsEnabled(LogLevel.Trace))
				{
					_logger.LogTrace("Predictions {0}", predictions.Select(p => new { p.Label.Name, p.Score }));
				}

				var predictionsOfInterest = predictions.Where(p => p.Score > _applicationSettings.PredicitionScoreThreshold)
												.Select(c => c.Label.Name)
												.Intersect(_applicationSettings.PredictionLabelsOfInterest, StringComparer.OrdinalIgnoreCase);

				if (predictionsOfInterest.Any())
				{
					if (_logger.IsEnabled(LogLevel.Trace))
					{
						_logger.LogTrace("Predictions of interest {0}", predictionsOfInterest.ToList());
					}

					var predictionsTally = predictions.GroupBy(p => p.Label.Name)
											.Select(p => new
											{
												Label = p.Key,
												Count = p.Count()
											});

					if (_logger.IsEnabled(LogLevel.Information))
					{
						_logger.LogInformation("Predictions tally {0}", predictionsTally.ToList());
					}

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
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Camera image download, post procesing, telemetry failed");
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

					_logger.LogTrace("Hub:{0} DeviceID:{1} RegistrationID:{2} Status:{3}",result.AssignedHub,result.DeviceId, result.RegistrationId,result.Status);
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
	}
}