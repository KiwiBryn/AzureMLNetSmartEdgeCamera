//---------------------------------------------------------------------------------
// Copyright (c) March 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
namespace devMobile.IoT.MachineLearning.SmartEdgeCameraAzureIoTService
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
#if AZURE_STORAGE_IMAGE_UPLOAD
					services.Configure<AzureStorageSettings>(hostContext.Configuration.GetSection("AzureStorage"));
#endif
#if AZURE_IOT_HUB_CONNECTION
					services.Configure<AzureIoTHubSettings>(hostContext.Configuration.GetSection("AzureIoTHub"));
#endif
#if AZURE_IOT_HUB_DPS_CONNECTION
					services.Configure<AzureIoTHubDpsSettings>(hostContext.Configuration.GetSection("AzureIoTHubDPS"));
#endif
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
