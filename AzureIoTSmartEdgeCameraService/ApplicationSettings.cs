//---------------------------------------------------------------------------------
// Copyright (c) February 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
namespace devMobile.IoT.MachineLearning.AzureIoTSmartEdgeCameraService
{
	using System;
	using System.Collections.Generic;

	public class ApplicationSettings
	{
		public string DeviceId { get; set; }

		public TimeSpan ImageTimerDue { get; set; }
		public TimeSpan ImageTimerPeriod { get; set; }

		public string ImageCameraFilepath { get; set; }
		public string ImageMarkedUpFilepath { get; set; }

      public string ImageMarkUpFontPath { get; set; }
      public int ImageMarkUpFontSize { get; set; }

      public string YoloV5ModelPath { get; set; }
		public double PredictionScoreThreshold { get; set; }
		public List<String> PredictionLabelsOfInterest { get; set; }
	}

	public class SecurityCameraSettings
	{
		public string CameraUrl { get; set; }
		public string CameraUserName { get; set; }
		public string CameraUserPassword { get; set; }
	}

	public class RaspberryPICameraSettings
	{
		public int Rotation { get; set; }
		public int ProcessWaitForExit { get; set; }
	}
}
