//---------------------------------------------------------------------------------
// Copyright (c) January 2022, devMobile Software
// 
// https://www.gnu.org/licenses/#AGPL
//
//---------------------------------------------------------------------------------
using System;

namespace devMobile.IoT.MachineLearning.RaspberryPICameraImageTimer.Model
{
	public class ApplicationSettings
	{
		public TimeSpan ImageImageTimerDue { get; set; }
		public TimeSpan ImageTimerPeriod { get; set; }

		public string ImageFilepathLocal { get; set; }
	}
}
