//---------------------------------------------------------------------------------
// Copyright (c) February 2022, devMobile Software
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace devMobile.IoT.MachineLearning.AzureIoTSmartEdgeCameraService
{
	public class Program
	{
		public static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			 Host.CreateDefaultBuilder(args)
				.ConfigureServices((hostContext, services) =>
				{
					services.Configure<ApplicationSettings>(hostContext.Configuration.GetSection("Application"));
					services.Configure<SecurityCameraSettings>(hostContext.Configuration.GetSection("SecurityCamera"));
					services.Configure<RaspberryPICameraSettings>(hostContext.Configuration.GetSection("RaspberryPICamera"));
				})
				.ConfigureLogging(logging =>
				{
					logging.ClearProviders();
					logging.AddSimpleConsole(c => c.TimestampFormat = "[HH:mm:ss.ff]");
				})
				.UseSystemd()
				.ConfigureServices((hostContext, services) =>
				{
					services.AddHostedService<Worker>();
				});
	}
}
