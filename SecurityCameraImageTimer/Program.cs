//---------------------------------------------------------------------------------
// Copyright (c) January 2022, devMobile Software
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

                NetworkCredential networkCredential = new NetworkCredential()
                {
                    UserName = _applicationSettings.CameraUserName,
                    Password = _applicationSettings.CameraUserPassword,
                };

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