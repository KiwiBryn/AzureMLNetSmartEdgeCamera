//---------------------------------------------------------------------------------
// Copyright (c) February 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
namespace devMobile.IoT.MachineLearning.SmartEdgeCameraAzureStorageService
{
	using Microsoft.Extensions.DependencyInjection;
	using Microsoft.Extensions.Hosting;
	using Microsoft.Extensions.Logging;

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
#if CAMERA_SECURITY
					services.Configure<SecurityCameraSettings>(hostContext.Configuration.GetSection("SecurityCamera"));
#endif
#if CAMERA_RASPBERRY_PI
					services.Configure<RaspberryPICameraSettings>(hostContext.Configuration.GetSection("RaspberryPICamera"));
#endif
					services.Configure<AzureStorageSettings>(hostContext.Configuration.GetSection("AzureStorage"));
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
