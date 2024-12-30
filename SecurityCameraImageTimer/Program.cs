//---------------------------------------------------------------------------------
// Copyright (c) January 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;


namespace devMobile.IoT.MachineLearning.SecurityCameraImageTimer
{
    class Program
    {
        private static Model.ApplicationSettings _applicationSettings;
        private static bool _cameraBusy = false;
        private static HttpClient _httpClient;

        static async Task Main(string[] args)
        {
            Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} SecurityCameraImage starting");

            try
            {
                // load the app settings into configuration
                var configuration = new ConfigurationBuilder()
                     .AddJsonFile("appsettings.json", false, true)
                     .AddUserSecrets<Program>()
                     .Build();

                _applicationSettings = configuration.GetSection("ApplicationSettings").Get<Model.ApplicationSettings>();

                NetworkCredential networkCredential = new NetworkCredential(_applicationSettings.CameraUserName, _applicationSettings.CameraUserPassword);

                _httpClient = new HttpClient(new HttpClientHandler { PreAuthenticate = true, Credentials = networkCredential });

                Timer imageUpdatetimer = new Timer(ImageUpdateTimerCallback, null, _applicationSettings.ImageTimerDue, _applicationSettings.ImageTimerPeriod);

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

        private static async void ImageUpdateTimerCallback(object state)
        {
            // Just incase - stop code being called while photo already in progress
            if (_cameraBusy)
            {
                return;
            }
            _cameraBusy = true;

            try
            {
                Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss.fff} Security Camera Image download start");

                using (Stream cameraStream = await _httpClient.GetStreamAsync(_applicationSettings.CameraUrl))
                using (Stream fileStream = File.Create(_applicationSettings.ImageFilepathLocal))
                {
                    await cameraStream.CopyToAsync(fileStream);
                }

                Console.WriteLine($" {DateTime.UtcNow:yy-MM-dd HH:mm:ss:fff} Security Camera Image download done");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.UtcNow:yy-MM-dd HH:mm:ss} Security camera image download failed {ex.Message}");
            }
            finally
            {
                _cameraBusy = false;
            }
        }
    }
}