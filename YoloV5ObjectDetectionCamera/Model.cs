//---------------------------------------------------------------------------------
// Copyright (c) January 2021, devMobile Software
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

		public string ImageInputFilenameLocal { get; set; }
		public string ImageOutputFilenameLocal { get; set; }

		public string YoloV5ModelPath { get; set; }

		public double PredicitionScoreThreshold { get; set; }

#if PREDICTION_CLASSES_OF_INTEREST
		public List<String> PredictionLabelsOfInterest { get; set; }
#endif
	}
}
