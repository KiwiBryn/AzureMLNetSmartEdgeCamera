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
namespace devMobile.IoT.MachineLearning.SmartEdgeCameraAzureStorageService
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

		public bool ImageCameraUpload { get; set; }
		public bool ImageMarkedupUpload { get; set; }

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

	public class AzureStorageSettings
	{
		public string ConnectionString { get; set; }
		public string ImageCameraFilenameFormat { get; set; }
		public string ImageMarkedUpFilenameFormat { get; set; }
	}
}
