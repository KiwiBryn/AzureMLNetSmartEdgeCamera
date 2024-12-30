//---------------------------------------------------------------------------------
// Copyright (c) January 2021, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace devMobile.IoT.MachineLearning.AzureIoTSmartEdgeCamera.Model
{
	public class ApplicationSettings
	{
		public string DeviceId { get; set; }

#if GPIO_SUPPORT
		public int LedPinNumer { get; set; }
#endif

		public TimeSpan ImageTimerDue { get; set; }
		public TimeSpan ImageTimerPeriod { get; set; }

#if CAMERA_SECURITY
		public string CameraUrl { get; set; }
		public string CameraUserName { get; set; }
		public string CameraUserPassword { get; set; }
#endif

#if CAMERA_RASPBERRY_PI
		public int ProcessWaitForExit { get; set; }
#endif

		public string ImageInputFilenameLocal { get; set; }
		public string ImageOutputFilenameLocal { get; set; }

      public string ImageMarkUpFontPath { get; set; }
      public int ImageMarkUpFontSize { get; set; }


      public string YoloV5ModelPath { get; set; }

		public double PredictionScoreThreshold { get; set; }

#if PREDICTION_CLASSES_OF_INTEREST
		public List<String> PredictionLabelsOfInterest { get; set; }
#endif

#if AZURE_STORAGE_IMAGE_UPLOAD
		public string AzureStorageConnectionString { get; set; }
		public string AzureStorageImageInputFilenameFormat { get; set; }
		public string AzureStorageImageOutputFilenameFormat { get; set; }
#endif

#if AZURE_IOT_HUB_CONNECTION
		public string AzureIoTHubConnectionString { get; set; }
#endif

#if AZURE_IOT_HUB_DPS_CONNECTION
		public string AzureIoTHubDpsGlobalDeviceEndpoint { get; set; }
		public string AzureIoTHubDpsIDScope { get; set; }
		public string AzureIoTHubDpsGroupEnrollmentKey { get; set; }
#endif
	}
}
