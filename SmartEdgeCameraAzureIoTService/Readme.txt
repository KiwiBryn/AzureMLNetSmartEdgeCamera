Instructions for installing background service on Raspberry OS Copyright (c) February 2022, devMobile Software

Install service

Test in application directory
	/home/pi/.dotnet/dotnet SmartEdgeCameraAzureIoTService.dll

Make service directory
	sudo mkdir /usr/sbin/SmartEdgeCameraAzureIoTService

Copy files to service directory
	sudo cp *.* /usr/sbin/SmartEdgeCameraAzureIoTService

Copy .service file to systemd folderclear
	sudo cp SmartEdgeCameraAzureIoTService.service /etc/systemd/system/SmartEdgeCameraAzureIoTService.service
 
Force reload of systemd configuration
	sudo systemctl daemon-reload

Start the Azure IoT SmartEdge Camera service
	sudo systemctl start SmartEdgeCameraAzureIoTService

 

Uninstall service

	sudo systemctl stop SmartEdgeCameraAzureIoTService

	sudo rm /etc/systemd/system/SmartEdgeCameraAzureIoTService.service

	sudo systemctl daemon-reload

	sudo rm /usr/sbin/SmartEdgeCameraAzureIoTService/*.*

	sudo rmdir /usr/sbin/SmartEdgeCameraAzureIoTService

	See what is happening

	journalctl -xe
