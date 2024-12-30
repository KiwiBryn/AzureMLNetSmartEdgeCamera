//---------------------------------------------------------------------------------
// Copyright (c) February 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
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
