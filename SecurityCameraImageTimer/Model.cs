//---------------------------------------------------------------------------------
// Copyright (c) January 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
using System;

namespace devMobile.IoT.MachineLearning.SecurityCameraImageTimer.Model
{
	public class ApplicationSettings
	{
		public TimeSpan ImageTimerDue { get; set; }
		public TimeSpan ImageTimerPeriod { get; set; }

		public string CameraUrl { get; set; }
		public string CameraUserName { get; set; }
		public string CameraUserPassword { get; set; }

		public string ImageFilepathLocal { get; set; }
	}
}
