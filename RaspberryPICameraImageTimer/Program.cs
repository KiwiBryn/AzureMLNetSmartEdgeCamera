//---------------------------------------------------------------------------------
// Copyright (c) January 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;

namespace devMobile.IoT.MachineLearning.RaspberryPICameraImageTimer
{
	class Program
	{
		private static Model.ApplicationSettings _applicationSettings;
		private static bool _cameraBusy = false;

		static async Task Main(string[] args)
		{
			Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} Raspberry PI camera image start");

			try
			{
				// load the app settings into configuration
				var configuration = new ConfigurationBuilder()
					 .AddJsonFile("appsettings.json", false, true)
					 .Build();

				_applicationSettings = configuration.GetSection("ApplicationSettings").Get<Model.ApplicationSettings>();

				Timer imageUpdatetimer = new Timer(ImageUpdateTimerCallback, null, _applicationSettings.ImageImageTimerDue, _applicationSettings.ImageTimerPeriod);

				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss} press <ctrl^c> to exit");

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
			// Just incase - stop code being called while photo already in progress
			if (_cameraBusy)
			{
				return;
			}

			try
			{
				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} RaspberryPI Camera Image capture start");

				using (Process process = new Process())
				{
					process.StartInfo.FileName = @"libcamera-jpeg";
					// V1 it works
					//process.StartInfo.Arguments = $"-o {_applicationSettings.ImageFilenameLocal}";
					// V3a Image right way up
					//process.StartInfo.Arguments = $"-o {_applicationSettings.ImageFilenameLocal} --vflip --hflip";
					// V3b Image right way up
					//process.StartInfo.Arguments = $"-o {_applicationSettings.ImageFilenameLocal} --rotation 180";
					// V4 Image no preview
					//process.StartInfo.Arguments = $"-o {_applicationSettings.ImageFilenameLocal} --rotation 180 --nopreview";
					// V5 Image no preview, no timeout
					process.StartInfo.Arguments = $"-o {_applicationSettings.ImageFilepathLocal} --nopreview -t 1 --rotation 180";
					//process.StartInfo.RedirectStandardOutput = true;
					// V2 No diagnostics
					process.StartInfo.RedirectStandardError = true;
					//process.StartInfo.UseShellExecute = false;
					//process.StartInfo.CreateNoWindow = true; 

					process.Start();

					if (!process.WaitForExit(10000) || (process.ExitCode != 0))
					{
						Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} RaspberryPI Camera image capture failed {process.ExitCode}");
					}
				}

				Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} RaspberryPI Camera image capture done");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} RaspberryPI image capture error {ex.Message}");
			}
			finally
			{
				_cameraBusy = false;
			}
		}
	}
}
