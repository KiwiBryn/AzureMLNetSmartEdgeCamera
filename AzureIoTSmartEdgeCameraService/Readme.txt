Instructions for installing background service on Raspberry OS Copyright (c) February 2022, devMobile Software

Install service

Test in application directory
	/home/pi/.dotnet/dotnet AzureIoTSmartEdgeCameraService.dll

Make service directory
	sudo mkdir /usr/sbin/AzureIoTSmartEdgeCameraService

Copy files to service directory
	sudo cp *.* /usr/sbin/AzureIoTSmartEdgeCameraService

Copy .service file to systemd folderclear
	sudo cp AzureIoTSmartEdgeCameraService.service /etc/systemd/system/AzureIoTSmartEdgeCameraService.service
 
Force reload of systemd configuration
	sudo systemctl daemon-reload

Start the Azure IoT SmartEdge Camera service
	sudo systemctl start AzureIoTSmartEdgeCameraService

 

Uninstall service

	sudo systemctl stop AzureIoTSmartEdgeCameraService

	sudo rm /etc/systemd/system/AzureIoTSmartEdgeCameraService.service

	sudo systemctl daemon-reload

	sudo rm /usr/sbin/AzureIoTSmartEdgeCameraService/*.*

	sudo rmdir /usr/sbin/AzureIoTSmartEdgeCameraService

	See what is happening

	journalctl -xe
