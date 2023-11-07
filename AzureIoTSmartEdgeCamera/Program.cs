//---------------------------------------------------------------------------------
// Copyright (c) December 2021, devMobile Software
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
namespace devMobile.IoT.MachineLearning.AzureIoTSmartEdgeCamera
{
   using System;
   using System.Collections.Generic;
#if GPIO_SUPPORT
	using System.Device.Gpio;
#endif
#if CAMERA_RASPBERRY_PI
	using System.Diagnostics;
#endif
   using System.Globalization;
#if CAMERA_SECURITY
   using System.IO;
   using System.Net;
   using System.Net.Http;
#endif
   using System.Linq;
#if AZURE_DEVICE_TWIN
	using System.Reflection;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
   using System.Security.Cryptography;
#endif
   using System.Text;
   using System.Threading;
   using System.Threading.Tasks;

#if AZURE_STORAGE_IMAGE_UPLOAD
   using Azure.Storage.Blobs;
#endif

#if AZURE_IOT_HUB_CONNECTION || AZURE_IOT_HUB_DPS_CONNECTION
   using Microsoft.Azure.Devices.Client;
#endif
#if AZURE_DEVICE_TWIN
	using Microsoft.Azure.Devices.Shared;
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
   using Microsoft.Azure.Devices.Shared;
   using Microsoft.Azure.Devices.Provisioning.Client;
   using Microsoft.Azure.Devices.Provisioning.Client.Transport;
#endif
   using Microsoft.Extensions.Configuration;

#if AZURE_IOT_HUB_CONNECTION || AZURE_IOT_HUB_DPS_CONNECTION
   using Newtonsoft.Json;
   using Newtonsoft.Json.Linq;
#endif

   using SixLabors.ImageSharp.PixelFormats;
   using SixLabors.ImageSharp;

#if OUTPUT_IMAGE_MARKUP
   using SixLabors.ImageSharp.Processing;
   using SixLabors.ImageSharp.Drawing.Processing;
   using SixLabors.Fonts;
#endif

   using Yolov5Net.Scorer;
   using Yolov5Net.Scorer.Models;

   // Compile time options
   // GPIO_SUPPORT
   // CAMERA_RASPBERRY_PI or CAMERA_SECURITY both would be bad
   // PREDICTION_CLASSES
   // OUTPUT_IMAGE_MARKUP
   // PREDICTION_CLASSES_OF_INTEREST
   // AZURE_IOT_HUB_CONNECTION or AZURE_IOT_HUB_DPS_CONNECTION both would be bad
   // AZURE_STORAGE_IMAGE_UPLOAD

   class Program
   {
      private static Model.ApplicationSettings _applicationSettings;
      private static bool _cameraBusy = false;
#if CAMERA_SECURITY
      private static HttpClient _httpClient;
#endif
#if AZURE_IOT_HUB_CONNECTION || AZURE_IOT_HUB_DPS_CONNECTION
      private static DeviceClient _deviceClient;
#endif
      private static YoloScorer<YoloCocoP5Model> _scorer = null;
#if GPIO_SUPPORT
		private static GpioController _gpiocontroller;
#endif

      static async Task Main(string[] args)
      {
         Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} AzureIoT Smart Edge Camera starting");
#if GPIO_SUPPORT
			Console.WriteLine(" GPIO support enabled");
#else
         Console.WriteLine(" GPIO support disabled");
#endif
#if PREDICTION_CLASSES
         Console.WriteLine(" Prediction classes display support enabled");
#else
			Console.WriteLine(" Prediction classes display support disabled");
#endif
#if OUTPUT_IMAGE_MARKUP
         Console.WriteLine(" Output image prediction markup support enabled");
#else
			Console.WriteLine(" Output image prediction markup support disabled");
#endif
#if PREDICTION_CLASSES_OF_INTEREST
         Console.WriteLine(" Prediction classes of interest support enabled");
#else
			Console.WriteLine(" Prediction classes of interest support disabled");
#endif
#if AZURE_IOT_HUB_CONNECTION
			Console.WriteLine(" Azure IoT Hub support enabled");
#else
         Console.WriteLine(" Azure IoT Hub support disabled");
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
         Console.WriteLine(" Azure IoT Hub DPS support enabled");
#else
			Console.WriteLine(" Azure IoT Hub DPS support disabled");
#endif
#if AZURE_STORAGE_IMAGE_UPLOAD
         Console.WriteLine(" Azure Storage image upload enabled");
#else
			Console.WriteLine(" Azure Storage image upload disabled");
#endif
         Console.WriteLine();

         try
         {
            // load the app settings into configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", false, true)
                .AddUserSecrets<Program>()
                .Build();

            _applicationSettings = configuration.GetSection("ApplicationSettings").Get<Model.ApplicationSettings>();

#if CAMERA_SECURITY
            NetworkCredential networkCredential = new NetworkCredential(_applicationSettings.CameraUserName, _applicationSettings.CameraUserPassword);

            _httpClient = new HttpClient(new HttpClientHandler { PreAuthenticate = true, Credentials = networkCredential });
#endif

#if AZURE_IOT_HUB_CONNECTION
				_deviceClient = await AzureIoTHubConnection();
#endif

#if AZURE_IOT_HUB_DPS_CONNECTION
            _deviceClient = await AzureIoTHubDpsConnection();
#endif

#if AZURE_DEVICE_TWIN
				TwinCollection reportedProperties = new TwinCollection();

				// This is from the OS 
				reportedProperties["OSVersion"] = Environment.OSVersion.VersionString;
				reportedProperties["MachineName"] = Environment.MachineName;
				reportedProperties["ApplicationVersion"] = Assembly.GetAssembly(typeof(Program)).GetName().Version;

				await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
#endif

#if AZURE_DEVICE_TWIN
				await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateCallback, null);

				Twin twin = await _deviceClient.GetTwinAsync();

				Console.WriteLine($"Desired:{twin.Properties.Desired.ToJson()}");
				Console.WriteLine($"Reported:{twin.Properties.Reported.ToJson()}");
#endif

#if GPIO_SUPPORT
				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} GPIO setup start");

				_gpiocontroller = new GpioController(PinNumberingScheme.Logical);

				_gpiocontroller.OpenPin(_applicationSettings.LedPinNumer, PinMode.Output);
				_gpiocontroller.Write(_applicationSettings.LedPinNumer, PinValue.Low);

				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} GPIO setup done");
#endif

            Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} YoloV5 model setup start");

            _scorer = new YoloScorer<YoloCocoP5Model>(_applicationSettings.YoloV5ModelPath);

            Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} YoloV5 model setup done");


            Timer imageUpdatetimer = new Timer(ImageUpdateTimerCallback, null, _applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

            Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} press <ctrl^c> to exit");
            Console.WriteLine();

            try
            {
               await Task.Delay(Timeout.Infinite);
            }
            catch (TaskCanceledException)
            {
               Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Application shutown requested");
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Application shutown failure {ex.Message}");
         }
         finally
         {
#if GPIO_SUPPORT
				_gpiocontroller?.Dispose();
#endif
#if AZURE_IOT_HUB_CONNECTION || AZURE_IOT_HUB_DPS_CONNECTION
            _deviceClient?.Dispose();
#endif
         }
      }

#if AZURE_DEVICE_TWIN
		private static Task DesiredPropertyUpdateCallback(TwinCollection desiredProperties, object userContext)
		{
			Console.WriteLine($"desiredProperties {desiredProperties.ToJson()}");
		}
#endif

      private static async void ImageUpdateTimerCallback(object state)
      {
         DateTime requestAtUtc = DateTime.UtcNow;

         // Just incase - stop code being called while photo already in progress
         if (_cameraBusy)
         {
            return;
         }
         _cameraBusy = true;

         Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Image processing start");

         try
         {
#if CAMERA_SECURITY
            await SecurityCameraImageCaptureAsync();
#endif

#if CAMERA_RASPBERRY_PI
				RaspberryPICameraImageCapture();
#endif

#if AZURE_STORAGE_IMAGE_UPLOAD
            await AzureStorageImageUpload(requestAtUtc, _applicationSettings.ImageInputFilenameLocal, _applicationSettings.AzureStorageImageInputFilenameFormat);
#endif
            List<YoloPrediction> predictions;

            // Process the image on local file system
            using (var image = await Image.LoadAsync<Rgba32>(_applicationSettings.ImageInputFilenameLocal))
            {

               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} YoloV5 inferencing start");
               predictions = _scorer.Predict(image);
               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} YoloV5 inferencing done");

#if OUTPUT_IMAGE_MARKUP
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

                  image.Mutate(a => a.DrawText($"{prediction.Label.Name} ({score})", font, prediction.Label.Color, new PointF(x, y)));
               }

               image.Save(_applicationSettings.ImageOutputFilenameLocal);
#endif
            }

#if PREDICTION_CLASSES_OF_INTEREST
            IEnumerable<string> predictionsOfInterest= predictions.Where(p=>p.Score > _applicationSettings.PredictionScoreThreshold).Select(c => c.Label.Name).Intersect(_applicationSettings.PredictionLabelsOfInterest, StringComparer.OrdinalIgnoreCase);

            if (predictionsOfInterest.Any())
            {
               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} Camera image comtains {String.Join(",", predictionsOfInterest)}");
            }

#if AZURE_STORAGE_IMAGE_UPLOAD
            if (predictionsOfInterest.Any())
            {
               await AzureStorageImageUpload(requestAtUtc, _applicationSettings.ImageOutputFilenameLocal, _applicationSettings.AzureStorageImageOutputFilenameFormat);
            }
#endif

#if GPIO_SUPPORT
				if (predictionsOfInterest.Any())
				{
					_gpiocontroller.Write(_applicationSettings.LedPinNumer, PinValue.High);
				}
				else
				{
					_gpiocontroller.Write(_applicationSettings.LedPinNumer, PinValue.Low);
				}
#endif
#endif

#if AZURE_IOT_HUB_CONNECTION || AZURE_IOT_HUB_DPS_CONNECTION
            await AzureIoTHubTelemetry(requestAtUtc, predictions);
#endif
         }
         catch (Exception ex)
         {
            Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Camera image download, post procesing, image upload, or telemetry failed {ex.Message}");
         }
         finally
         {
            _cameraBusy = false;
         }

         TimeSpan duration = DateTime.UtcNow - requestAtUtc;

         Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Image processing done {duration.TotalSeconds:f2} sec");
         Console.WriteLine();
      }

#if CAMERA_SECURITY
      private static async Task SecurityCameraImageCaptureAsync()
      {
         Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Security Camera Image download start");

         using (Stream cameraStream = await _httpClient.GetStreamAsync(_applicationSettings.CameraUrl))
         using (Stream fileStream = File.Create(_applicationSettings.ImageInputFilenameLocal))
         {
            await cameraStream.CopyToAsync(fileStream);
         }

         Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Security Camera Image download done");
      }
#endif

#if CAMERA_RASPBERRY_PI
		private static void RaspberryPIImageCapture()
		{
			Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Raspberry PI Image capture start");

			using (Process process = new Process())
			{
				process.StartInfo.FileName = @"libcamera-jpeg";
				process.StartInfo.Arguments = $"-o {_applicationSettings.InputImageFilenameLocal} --nopreview -t1 --rotation 180";
				process.StartInfo.RedirectStandardError = true;

				process.Start();

				if (!process.WaitForExit(_applicationSettings.ProcessWaitForExit) || (process.ExitCode != 0))
				{
					Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image update failure {process.ExitCode}");
				}
			}

			Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Raspberry PI Image capture done");
		}
#endif

#if AZURE_IOT_HUB_CONNECTION
		private static async Task<DeviceClient> AzureIoTHubConnection()
		{
			Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} Azure IoT Hub connection start");

			DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(_applicationSettings.AzureIoTHubConnectionString, _applicationSettings.DeviceId);

			await deviceClient.OpenAsync();

			Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} Azure IoT Hub connection done");

			return deviceClient;
		}
#endif

#if AZURE_IOT_HUB_DPS_CONNECTION
      private static async Task<DeviceClient> AzureIoTHubDpsConnection()
      {
         string deviceKey;
         DeviceClient deviceClient;

         Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} Azure IoT Hub DPS connection start");

         using (var hmac = new HMACSHA256(Convert.FromBase64String(_applicationSettings.AzureIoTHubDpsGroupEnrollmentKey)))
         {
            deviceKey = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(_applicationSettings.DeviceId)));
         }

         using (var securityProvider = new SecurityProviderSymmetricKey(_applicationSettings.DeviceId, deviceKey, null))
         {
            using (var transport = new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly))
            {
               ProvisioningDeviceClient provClient = ProvisioningDeviceClient.Create(_applicationSettings.AzureIoTHubDpsGlobalDeviceEndpoint, _applicationSettings.AzureIoTHubDpsIDScope, securityProvider, transport);

               DeviceRegistrationResult result = await provClient.RegisterAsync();

               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} Hub:{result.AssignedHub} DeviceID:{result.DeviceId} RegistrationID:{result.RegistrationId} Status:{result.Status}");
               if (result.Status != ProvisioningRegistrationStatusType.Assigned)
               {
                  Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} DeviceID:{result.DeviceId} {result.Status} already assigned");
               }

               IAuthenticationMethod authentication = new DeviceAuthenticationWithRegistrySymmetricKey(result.DeviceId, (securityProvider as SecurityProviderSymmetricKey).GetPrimaryKey());

               deviceClient = DeviceClient.Create(result.AssignedHub, authentication, TransportType.Amqp_Tcp_Only);
            }
         }

         await deviceClient.OpenAsync();

         Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} Azure IoT Hub DPS connection done");

         return deviceClient;
      }
#endif

#if AZURE_IOT_HUB_CONNECTION || AZURE_IOT_HUB_DPS_CONNECTION
      public static async Task AzureIoTHubTelemetry(DateTime requestAtUtc, List<YoloPrediction> predictions)
      {
         JObject telemetryDataPoint = new JObject();

         Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} AzureIoTHubClient SendEventAsync prediction information start");

         foreach (var predictionTally in predictions.Where(p => p.Score >= _applicationSettings.PredictionScoreThreshold).GroupBy(p => p.Label.Name)
                     .Select(p => new
                     {
                        Label = p.Key,
                        Count = p.Count()
                     }))
         {
            Console.WriteLine("  {0} {1}", predictionTally.Label, predictionTally.Count);

            telemetryDataPoint.Add(predictionTally.Label, predictionTally.Count);
         }

         try
         {
            using (Message message = new Message(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(telemetryDataPoint))))
            {
               message.Properties.Add("iothub-creation-time-utc", requestAtUtc.ToString("s", CultureInfo.InvariantCulture));

               await _deviceClient.SendEventAsync(message);
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} AzureIoTHubClient SendEventAsync cow counting failed {ex.Message}");
         }

         Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} AzureIoTHubClient SendEventAsync prediction information finish");
      }
#endif

#if AZURE_STORAGE_IMAGE_UPLOAD
      public static async Task AzureStorageImageUpload(DateTime requestAtUtc, string imageFilenameLocal, string format)
      {
         Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} latest image upload start");

         BlobServiceClient imageBlobServiceClient = new BlobServiceClient(_applicationSettings.AzureStorageConnectionString);
         BlobContainerClient imagecontainerClient = imageBlobServiceClient.GetBlobContainerClient(_applicationSettings.DeviceId.ToLower());

         await imagecontainerClient.CreateIfNotExistsAsync();

         string imageFilenameCloud = string.Format(format, requestAtUtc);

         if (!string.IsNullOrWhiteSpace(imageFilenameCloud))
         {

            BlobClient blobClientHistory = imagecontainerClient.GetBlobClient(imageFilenameCloud);

            await blobClientHistory.UploadAsync(imageFilenameLocal, true);

         }

         Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} latest image upload done");
      }
#endif
   }
}