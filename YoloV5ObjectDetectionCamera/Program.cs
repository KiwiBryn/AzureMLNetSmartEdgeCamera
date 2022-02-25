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
// 
// Error:
// An assembly specified in the application dependencies manifest (ObjectDetectionCamera.deps.json) was not found:
//    package: 'System.Drawing.Common', version: '5.0.3'
//
//		sudo apt-get install libgdiplus
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
using System.Drawing;
#if SECURITY_CAMERA
	using System.Net;
#endif
#if PREDICTION_CLASSES || PREDICTION_CLASSES_OF_INTEREST
	using System.Linq;
#endif
	using System.Threading;
	using System.Threading.Tasks;

	using Microsoft.Extensions.Configuration;

	using Yolov5Net.Scorer;
	using Yolov5Net.Scorer.Models;

	class Program
	{
		private static Model.ApplicationSettings _applicationSettings;
		private static bool _cameraBusy = false;
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

		private static void ImageUpdateTimerCallback(object state)
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

				NetworkCredential networkCredential = new NetworkCredential()
				{
					UserName = _applicationSettings.CameraUserName,
					Password = _applicationSettings.CameraUserPassword,
				};

				using (WebClient client = new WebClient())
				{
					client.Credentials = networkCredential;

					client.DownloadFile(_applicationSettings.CameraUrl, _applicationSettings.ImageInputFilenameLocal);
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
				using (Image image = Image.FromFile(_applicationSettings.ImageInputFilenameLocal))
				{
					Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} YoloV5 inferencing start");
					predictions = _scorer.Predict(image);
					Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} YoloV5 inferencing done");

#if OUTPUT_IMAGE_MARKUP
					using (Graphics graphics = Graphics.FromImage(image))
					{
						Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image markup start");

						foreach (var prediction in predictions) // iterate predictions to draw results
						{
							double score = Math.Round(prediction.Score, 2);

							graphics.DrawRectangles(new Pen(prediction.Label.Color, 1), new[] { prediction.Rectangle });

							var (x, y) = (prediction.Rectangle.X - 3, prediction.Rectangle.Y - 23);

							graphics.DrawString($"{prediction.Label.Name} ({score})", new Font("Consolas", 16, GraphicsUnit.Pixel), new SolidBrush(prediction.Label.Color), new PointF(x, y));
						}

						image.Save(_applicationSettings.ImageOutputFilenameLocal);

						Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image markup done");
					}
#endif
				}

#if PREDICTION_CLASSES
				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image classes start");
				foreach (var prediction in predictions)
				{
					Console.WriteLine($"  Name:{prediction.Label.Name} Score:{prediction.Score:f2} Valid:{prediction.Score > _applicationSettings.PredicitionScoreThreshold}");
				}
				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Image classes done");
#endif

#if PREDICTION_CLASSES_OF_INTEREST
				IEnumerable<string> predictionsOfInterest= predictions.Where(p=>p.Score > _applicationSettings.PredicitionScoreThreshold).Select(c => c.Label.Name).Intersect(_applicationSettings.PredictionLabelsOfInterest, StringComparer.OrdinalIgnoreCase);

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