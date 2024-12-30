//---------------------------------------------------------------------------------
// Copyright (c) December 2021, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
// On *nix system like PE100 ubuntu or RPI
// 
//	cd YoloV5ObjectDetectionCamera
//
//	dotnet YoloV5ObjectDetectionCamera.dll
//
//---------------------------------------------------------------------------------
namespace devMobile.IoT.MachineLearning.YoloV5ObjectDetectionCamera
{
   using System;
   using System.Collections.Generic;
#if GPIO_SUPPORT
	using System.Device.Gpio;
#endif
#if RASPBERRY_PI_CAMERA
	using System.Diagnostics;
#endif
#if SECURITY_CAMERA
   using System.IO;
   using System.Net;
   using System.Net.Http;
#endif
#if PREDICTION_CLASSES || PREDICTION_CLASSES_OF_INTEREST
   using System.Linq;
#endif
   using System.Threading;
   using System.Threading.Tasks;

   using Microsoft.Extensions.Configuration;

   using SixLabors.Fonts;
   using SixLabors.ImageSharp;
   using SixLabors.ImageSharp.Drawing.Processing;
   using SixLabors.ImageSharp.PixelFormats;
   using SixLabors.ImageSharp.Processing;

   using Yolov5Net.Scorer;
   using Yolov5Net.Scorer.Models;

   class Program
   {
      private static Model.ApplicationSettings _applicationSettings;
      private static bool _cameraBusy = false;
#if SECURITY_CAMERA
      private static HttpClient _httpClient;
#endif
      private static YoloScorer<YoloCocoP5Model> _scorer = null;
#if GPIO_SUPPORT
		private static GpioController _gpiocontroller;
#endif

      static async Task Main(string[] args)
      {
         Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} YoloV5ObjectDetectionCamera starting");

         try
         {
            // load the app settings into configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", false, true)
                .AddUserSecrets<Program>()
                .Build();

            _applicationSettings = configuration.GetSection("ApplicationSettings").Get<Model.ApplicationSettings>();

#if SECURITY_CAMERA
            NetworkCredential networkCredential = new NetworkCredential(_applicationSettings.CameraUserName, _applicationSettings.CameraUserPassword);

            _httpClient = new HttpClient(new HttpClientHandler { PreAuthenticate = true, Credentials = networkCredential });
#endif

#if GPIO_SUPPORT
				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} GPIO setup start");

				_gpiocontroller = new GpioController(PinNumberingScheme.Logical);

				_gpiocontroller.OpenPin(_applicationSettings.LedPinNumer, PinMode.Output);
				_gpiocontroller.Write(_applicationSettings.LedPinNumer, PinValue.Low);

				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} GPIO setup done");
#endif

            _scorer = new YoloScorer<YoloCocoP5Model>(_applicationSettings.YoloV5ModelPath);

            Timer imageUpdatetimer = new Timer(ImageUpdateTimerCallback, null, _applicationSettings.ImageImageTimerDue, _applicationSettings.ImageTimerPeriod);

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
            Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Application shutown failure {ex.Message}", ex);
         }
      }

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
#if SECURITY_CAMERA
            Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Security Camera Image download start");

            using (Stream cameraStream = await _httpClient.GetStreamAsync(_applicationSettings.CameraUrl))
            using (Stream fileStream = File.Create(_applicationSettings.ImageInputFilenameLocal))
            {
               await cameraStream.CopyToAsync(fileStream);
            }

            Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Security Camera Image download done");
#endif

#if RASPBERRY_PI_CAMERA
				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Raspberry PI Image capture start");

				using (Process process = new Process())
				{
					process.StartInfo.FileName = @"libcamera-jpeg";
					process.StartInfo.Arguments = $"-o {_applicationSettings.ImageInputFilenameLocal} --nopreview -t1 --rotation 180";
					process.StartInfo.RedirectStandardError = true;

					process.Start();

					if (!process.WaitForExit(_applicationSettings.ProcessWaitForExit) || (process.ExitCode != 0))
					{
						Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image update failure {process.ExitCode}");
					}
				}

				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Raspberry PI Image capture done");
#endif

            List<YoloPrediction> predictions;

            // Process the image on local file system
            using (Image<Rgba32> image = await Image.LoadAsync<Rgba32>(_applicationSettings.ImageInputFilenameLocal))
            {
               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} YoloV5 inferencing start");
               predictions = _scorer.Predict(image);
               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} YoloV5 inferencing done");

#if OUTPUT_IMAGE_MARKUP
               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image markup start");

               var font = new Font(new FontCollection().Add(_applicationSettings.ImageOutputMarkupFontPath), _applicationSettings.ImageOutputMarkupFontSize);

               foreach (var prediction in predictions) // iterate predictions to draw results
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

               await image.SaveAsJpegAsync(_applicationSettings.ImageOutputFilenameLocal);

               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image markup done");
#endif
            }


#if PREDICTION_CLASSES
            Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image classes start");
            foreach (var prediction in predictions)
            {
               Console.WriteLine($"  Name:{prediction.Label.Name} Score:{prediction.Score:f2} Valid:{prediction.Score > _applicationSettings.PredictionScoreThreshold}");
            }
            Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image classes done");
#endif

#if PREDICTION_CLASSES_OF_INTEREST
            IEnumerable<string> predictionsOfInterest = predictions.Where(p => p.Score > _applicationSettings.PredictionScoreThreshold).Select(c => c.Label.Name).Intersect(_applicationSettings.PredictionLabelsOfInterest, StringComparer.OrdinalIgnoreCase);

            if (predictionsOfInterest.Any())
            {
               Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} Camera image comtains {String.Join(",", predictionsOfInterest)}");
            }

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
         }
         catch (Exception ex)
         {
            Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Camera image download, upload or post procesing failed {ex.Message}");
         }
         finally
         {
            _cameraBusy = false;
         }

         TimeSpan duration = DateTime.UtcNow - requestAtUtc;

         Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Image processing done {duration.TotalSeconds:f2} sec");
         Console.WriteLine();
      }
   }
}