//---------------------------------------------------------------------------------
// Copyright (c) February 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
namespace devMobile.IoT.MachineLearning.SmartEdgeCameraAzureStorageService
{
   using System;
   using System.Collections.Generic;
#if CAMERA_SECURITY
   using System.IO;
#endif
#if CAMERA_RASPBERRY_PI
	using System.Diagnostics;
#endif
   using System.Linq;
#if CAMERA_SECURITY
   using System.Net;
   using System.Net.Http;
#endif
   using System.Threading;
   using System.Threading.Tasks;

   using Azure.Storage.Blobs;
   using Azure.Storage.Blobs.Models;

   using Microsoft.Extensions.Hosting;
   using Microsoft.Extensions.Logging;
   using Microsoft.Extensions.Options;

   using SixLabors.Fonts;
   using SixLabors.ImageSharp;
   using SixLabors.ImageSharp.Drawing.Processing;
   using SixLabors.ImageSharp.PixelFormats;
   using SixLabors.ImageSharp.Processing;

   using Yolov5Net.Scorer;
   using Yolov5Net.Scorer.Models;

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
      private readonly AzureStorageSettings _azureStorageSettings;
      private BlobServiceClient _imageBlobServiceClient;
      private BlobContainerClient _imageContainerClient;

      private bool _cameraBusy = false;

      public Worker(ILogger<Worker> logger,
         IOptions<ApplicationSettings> applicationSettings,
#if CAMERA_SECURITY
         IOptions<SecurityCameraSettings> securityCameraSettings,
#endif
#if CAMERA_RASPBERRY_PI
			IOptions<RaspberryPICameraSettings> raspberryPICameraSettings,
#endif
         IOptions<AzureStorageSettings> azureStorageSettings
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
         _azureStorageSettings = azureStorageSettings.Value;
      }

      protected override async Task ExecuteAsync(CancellationToken stoppingToken)
      {
         _logger.LogInformation("Azure Storage Smart Edge Camera Service starting");

         try
         {
#if CAMERA_SECURITY
            NetworkCredential networkCredential = new NetworkCredential(_securityCameraSettings.CameraUserName, _securityCameraSettings.CameraUserPassword);

            _httpClient = new HttpClient(new HttpClientHandler { PreAuthenticate = true, Credentials = networkCredential });
#endif

            _logger.LogInformation("Azure Storage initialisation start");
            _imageBlobServiceClient = new BlobServiceClient(_azureStorageSettings.ConnectionString);
            _imageContainerClient = _imageBlobServiceClient.GetBlobContainerClient(_applicationSettings.DeviceId.ToLower());

            await _imageContainerClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
            _logger.LogInformation("Azure Storage initialisation done");

            Timer imageUpdateTimer = new Timer(ImageUpdateTimerCallback, null, _applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

            try
            {
               await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (TaskCanceledException)
            {
               _logger.LogInformation("Application shutdown requested");
            }
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Application startup failure");
         }
         finally
         {
         }

         _logger.LogInformation("Azure Storage Smart Edge Camera Service shutdown");
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
            await SecurityCameraImageCaptureAsync();
#endif
            List<YoloPrediction> predictions;

            using var image = await Image.LoadAsync<Rgba32>(_applicationSettings.ImageCameraFilepath);
            {
               using var scorer = new YoloScorer<YoloCocoP5Model>(_applicationSettings.YoloV5ModelPath);
               {
                  predictions = scorer.Predict(image);

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

            if (_logger.IsEnabled(LogLevel.Trace))
            {
               _logger.LogTrace("Predictions {0}", predictions.Select(p => new { p.Label.Name, p.Score }));
            }

            var predictionsOfInterest = predictions.Where(p => p.Score >= _applicationSettings.PredictionScoreThreshold).Select(c => c.Label.Name).Intersect(_applicationSettings.PredictionLabelsOfInterest, StringComparer.OrdinalIgnoreCase);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
               _logger.LogTrace("Predictions of interest {0}", predictionsOfInterest.ToList());
            }

            var predictionsTally = predictions.Where(p => p.Score >= _applicationSettings.PredictionScoreThreshold)
                                 .GroupBy(p => p.Label.Name)
                                 .Select(p => new
                                 {
                                    Label = p.Key,
                                    Count = p.Count()
                                 });

            if (predictionsOfInterest.Any())
            {
               BlobUploadOptions blobUploadOptions = new BlobUploadOptions()
               {
                  Tags = new Dictionary<string, string>()
               };

               foreach (var prediction in predictionsTally)
               {
                  blobUploadOptions.Tags.Add(prediction.Label, prediction.Count.ToString());
               }

               if (_applicationSettings.ImageCameraUpload)
               {
                  _logger.LogTrace("Image camera upload start");

                  string imageFilenameCloud = string.Format(_azureStorageSettings.ImageCameraFilenameFormat, requestAtUtc);

                  await _imageContainerClient.GetBlobClient(imageFilenameCloud).UploadAsync(_applicationSettings.ImageCameraFilepath, blobUploadOptions);

                  _logger.LogTrace("Image camera upload done");
               }

               if (_applicationSettings.ImageMarkedupUpload)
               {
                  _logger.LogTrace("Image marked-up upload start");

                  string imageFilenameCloud = string.Format(_azureStorageSettings.ImageMarkedUpFilenameFormat, requestAtUtc);

                  await _imageContainerClient.GetBlobClient(imageFilenameCloud).UploadAsync(_applicationSettings.ImageMarkedUpFilepath, blobUploadOptions);

                  _logger.LogTrace("Image marked-up upload done");
               }
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
               _logger.LogInformation("Predictions tally {0}", predictionsTally.ToList());
            }
         }
         catch (Exception ex)
         {
            _logger.LogError(ex, "Camera image download, post processing, or image upload failed");
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
   }
}
