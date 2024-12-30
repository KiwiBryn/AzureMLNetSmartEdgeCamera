//---------------------------------------------------------------------------------
// Copyright (c) January 2021, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
namespace devMobile.IoT.MachineLearning.YoloV5ObjectDetectionCamera.Model
{
	using System;
	using System.Collections.Generic;

	public class ApplicationSettings
	{
#if GPIO_SUPPORT
		public int LedPinNumer { get; set; }
#endif

		public TimeSpan ImageImageTimerDue { get; set; }
		public TimeSpan ImageTimerPeriod { get; set; }

#if SECURITY_CAMERA
		public string CameraUrl { get; set; }
		public string CameraUserName { get; set; }
		public string CameraUserPassword { get; set; }
#endif

#if RASPBERRY_PI_CAMERA
		public int ProcessWaitForExit { get; set; }
#endif

      public string ImageOutputMarkupFontPath { get; set; }
      public int ImageOutputMarkupFontSize { get; set; }

      public string ImageInputFilenameLocal { get; set; }
		public string ImageOutputFilenameLocal { get; set; }

		public string YoloV5ModelPath { get; set; }

		public double PredictionScoreThreshold { get; set; }

#if PREDICTION_CLASSES_OF_INTEREST
		public List<String> PredictionLabelsOfInterest { get; set; }
#endif
	}
}
