
// Program 'SC20260_WiFi_init_via_UsbStick'
// Example for GHI Electronics TinyCLR Board SC20260 with WiFi Module on Micro Bus Port 2
// 
// This is an adaption of @kirklynk 's (-https://github.com/KiwiBryn) way to initialize the WiFi credentials of the board via
// a config.json file provided in an USB Stick:
// -https://forums.ghielectronics.com/t/feather-powered-camera-pan-tilt/24037
// 

using System;
using System.IO;
using System.Threading;
using GHIElectronics.TinyCLR.Data.Json;
using GHIElectronics.TinyCLR.Devices.Network;
using GHIElectronics.TinyCLR.Devices.Storage;
using GHIElectronics.TinyCLR.IO;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.Spi;

namespace SC20260_WiFi_init_via_UsbStick
{
    class Program
    {
        static string _line2;
        static string _line3;

        static StorageController _storageController;
        static Configuration _configuration;

        static void Main()
        {
            var gpioController = GpioController.GetDefault();


            InitializeUsbHost();
            InitializeWifi(gpioController);
        }

        public class Configuration
        {
            public WifiNetwork[] WifiNetworks { get; set; }
            public double TimeZoneOffsetInMinutes { get; set; }
            public double DaylightSavingsTimeOffsetInMinutes { get; set; }

            public static object CreateInstance(string path, JToken token, Type type, string name, int length)
            {
                switch (name)
                {
                    case "WifiNetworks":
                        return new WifiNetwork[length];
                    default:
                        if (type == null && string.IsNullOrEmpty(name) && ((JObject)token).Contains("Ssid"))
                            return new WifiNetwork();
                        break;
                }
                return null;
            }

        }

        public class WifiNetwork
        {
            public string Password { get; set; }
            public string Ssid { get; set; }
            public bool IsConnected { get; set; }
        }

        public static DateTime GetNetworkTime(int CorrectLocalTime = 0)
        {
            const string ntpServer = "pool.ntp.org";
            var ntpData = new byte[48];
            ntpData[0] = 0x1B; //LeapIndicator = 0 (no warning), VersionNum = 3 (IPv4 only),
                               //    Mode = 3 (Client Mode)

            var addresses = System.Net.Dns.GetHostEntry(ntpServer).AddressList;
            var ipEndPoint = new System.Net.IPEndPoint(addresses[0], 123);
            var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);

            socket.Connect(ipEndPoint);

            System.Threading.Thread.Sleep(1); //Added to support TinyCLR OS.

            socket.Send(ntpData);
            socket.Receive(ntpData);
            socket.Close();

            ulong intPart = (ulong)ntpData[40] << 24 | (ulong)ntpData[41] << 16 |
                (ulong)ntpData[42] << 8 | (ulong)ntpData[43];

            ulong fractPart = (ulong)ntpData[44] << 24 | (ulong)ntpData[45] << 16 |
                (ulong)ntpData[46] << 8 | (ulong)ntpData[47];

            var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);

            var networkDateTime = (new System.DateTime(1900, 1, 1)).
                AddMilliseconds((long)milliseconds);

            return networkDateTime.AddHours(CorrectLocalTime);
        }


        private static void InitializeUsbHost()
        {
            //Initialize usb host controller
            GHIElectronics.TinyCLR.Devices.UsbHost.UsbHostController usbHost = GHIElectronics.TinyCLR.Devices.UsbHost.UsbHostController.GetDefault();
            usbHost.OnConnectionChangedEvent += (sender, e) =>
            {
                switch (e.DeviceStatus)
                {
                    case GHIElectronics.TinyCLR.Devices.UsbHost.DeviceConnectionStatus.Disconnected:
                        switch (e.Type)
                        {

                            case GHIElectronics.TinyCLR.Devices.UsbHost.BaseDevice.DeviceType.MassStorage:
                                if (_storageController != null)
                                    FileSystem.Unmount(_storageController.Hdc);
                                break;
                        }
                        break;
                    case GHIElectronics.TinyCLR.Devices.UsbHost.DeviceConnectionStatus.Connected:
                        switch (e.Type)
                        {
                            case GHIElectronics.TinyCLR.Devices.UsbHost.BaseDevice.DeviceType.MassStorage:

                                _storageController = StorageController.FromName(SC20260.StorageController.UsbHostMassStorage);
                                var driver = FileSystem.Mount(_storageController.Hdc);

                                var driveInfo = new DriveInfo(driver.Name);
                                if (File.Exists(Path.Combine(driveInfo.Name, "config.json")))
                                {
                                    _configuration = (Configuration)JsonConverter.DeserializeObject(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(driveInfo.Name, "config.json"))), typeof(Configuration), factory: Configuration.CreateInstance);
                                }
                                _line2 = string.Empty;
                                _line3 = "Looking for a network";

                                break;
                        }
                        break;
                    case GHIElectronics.TinyCLR.Devices.UsbHost.DeviceConnectionStatus.Bad:
                        break;
                    default:
                        break;
                }
            };
            usbHost.Enable();
        }

        private static void InitializeWifi(GpioController gpioController)
        {
            //var wifiEnablePin = gpioController.OpenPin(GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.PA8);
            var wifiEnablePin = gpioController.OpenPin(GHIElectronics.TinyCLR.Pins.SC20260.GpioPin.PI5);
            wifiEnablePin.SetDriveMode(GpioPinDriveMode.Output);
            wifiEnablePin.Write(GpioPinValue.High);

            var settings = new SpiConnectionSettings()
            {
                //ChipSelectLine = gpioController.OpenPin(GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.PD15),
                ChipSelectLine = gpioController.OpenPin(GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.PC13),
                ClockFrequency = 4000000,
                Mode = SpiMode.Mode0,
                ChipSelectType = SpiChipSelectType.Gpio,
                ChipSelectHoldTime = TimeSpan.FromTicks(10),
                ChipSelectSetupTime = TimeSpan.FromTicks(10)
            };

            SpiNetworkCommunicationInterfaceSettings netInterfaceSettings = new SpiNetworkCommunicationInterfaceSettings()
            {
                SpiApiName = GHIElectronics.TinyCLR.Pins.SC20100.SpiBus.Spi3,
                GpioApiName = GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.Id,
                SpiSettings = settings,
                //InterruptPin = gpioController.OpenPin(GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.PB12),
                InterruptPin = gpioController.OpenPin(GHIElectronics.TinyCLR.Pins.SC20260.GpioPin.PJ13),
                InterruptEdge = GpioPinEdge.FallingEdge,
                InterruptDriveMode = GpioPinDriveMode.InputPullUp,
                //ResetPin = gpioController.OpenPin(GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.PB13),
                ResetPin = gpioController.OpenPin(GHIElectronics.TinyCLR.Pins.SC20260.GpioPin.PI11),
                ResetActiveState = GpioPinValue.Low
            };
            var networkController = NetworkController.FromName("GHIElectronics.TinyCLR.NativeApis.ATWINC15xx.NetworkController");

            networkController.SetCommunicationInterfaceSettings(netInterfaceSettings);
            networkController.SetAsDefaultController();
            byte[] address = new byte[4];

            networkController.NetworkAddressChanged += (NetworkController sender, NetworkAddressChangedEventArgs e) =>
            {
                _line3 = "0.0.0.0";
                var ipProperties = sender.GetIPProperties();
                address = ipProperties.Address.GetAddressBytes();
                if (address[0] > 0)
                {
                    _line3 = $"{address[0]}.{address[1]}.{address[2]}.{address[3]}";
                    _line2 = $"{((WiFiNetworkInterfaceSettings)sender.ActiveInterfaceSettings).Ssid}";
                    GetNetworkTime();
                }

            };

            //Starting a new thread for monitoring the WiFi connection state.
            //Loop through a list of known networks from the configuration file 
            new Thread(() =>
            {
                int idx = 0;

                while (true)
                {
                    try
                    {
                        if (_configuration == null || _configuration.WifiNetworks.Length == 0)
                        {
                            Thread.Sleep(5000);
                            continue;
                        }

                        var network = _configuration.WifiNetworks[idx];
                        _line2 = "Connecting to AP";
                        _line3 = network.Ssid;

                        Thread.Sleep(1000);

                        try
                        {

                            WiFiNetworkInterfaceSettings wifiSettings = new WiFiNetworkInterfaceSettings()
                            {
                                Ssid = network.Ssid,
                                Password = network.Password,
                                DhcpEnable = true,
                                DynamicDnsEnable = true,
                                TlsEntropy = new byte[] { 0, 1, 2, 3 }
                            };
                            networkController.SetInterfaceSettings(wifiSettings);

                            //Try enabling WiFi interface
                            networkController.Enable();

                            _line2 = network.Ssid;

                            //Sleep for 5sec to give ample time fr wifi to connect
                            Thread.Sleep(2000);
                            network.IsConnected = true;
                            //Poll WiFi connection link every 5sec
                            while (networkController.GetLinkConnected())
                            {
                                Thread.Sleep(10000);
                            }
                        }
                        catch (ArgumentException ex)
                        {
                            //This is exception is thrown when failed to connect to network

                            _line2 = "Unable to connect to network";
                            Thread.Sleep(1000);
                            network.IsConnected = false;
                            idx++;
                            if (idx == _configuration.WifiNetworks.Length)
                                idx = 0;
                        }
                        catch (Exception ex)
                        {
                            _line2 = "Unable to connect to network";
                            network.IsConnected = false;
                            idx++;
                            if (idx == _configuration.WifiNetworks.Length)
                                idx = 0;
                            Thread.Sleep(1000);

                        }
                        networkController.Disable();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            networkController.Disable();
                        }
                        catch (Exception)
                        {
                            //Intentionally left blank;
                        }
                    }
                }
            }).Start();
        }
    }
}
