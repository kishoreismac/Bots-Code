using Microsoft.Azure.Devices.Client;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Bot.Connector.DirectLine.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace FaceAndEmoticonDetection
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        //Status LED variables
        private const int LED_PIN = 5;
        private GpioPin PinLED;

        //PIR Motion Detector variables
        private const int PIR_PIN = 16;
        private GpioPin PinPIR;
        // private MediaCap;
        
        private MediaCapture MediaCap;
        private bool IsInPictureCaptureMode;


        /// <summary>
        /// Callback function for any failures in MediaCapture operations
        /// </summary>
        /// <param name="currentCaptureObject"></param>
        /// <param name="currentFailure"></param>

        public MainPage()
        {
            this.InitializeComponent();

            //camera initilization
            InitilizeWebcam();
            InitializeGPIO();
            
            //Turn the Status LED on
            LightLED(true);

            // At this point, the application waits for motion to be detected by
            // the PIR sensor, which then calls the PinPIR_ValueChanged() fucntion

        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            InitializeBotConversations();
        }
        private void InitializeGPIO()
        {
            try
            {
                //Obtain a reference to the GPIO Controller
                var gpio = GpioController.GetDefault();

                // Show an error if there is no GPIO controller
                if (gpio == null)
                {
                    PinLED = null;
                    Debug.WriteLine("No GPIO controller found on this device.");
                    return;
                }

                //Open the GPIO port for LED
                PinLED = gpio.OpenPin(LED_PIN);

                //set the mode as Output (we are WRITING a signal to this port)
                PinLED.SetDriveMode(GpioPinDriveMode.Output);

                //Open the GPIO port for PIR motion sensor
                PinPIR = gpio.OpenPin(PIR_PIN);

                //PIR motion sensor - Ignore changes in value of less than 50ms
                PinPIR.DebounceTimeout = new TimeSpan(0, 0, 0, 0, 50);

                //set the mode as Input (we are READING a signal from this port)
                PinPIR.SetDriveMode(GpioPinDriveMode.Input);

                //wire the ValueChanged event to the PinPIR_ValueChanged() function
                //when this value changes (motion is detected), the function is called
                PinPIR.ValueChanged += PinPIR_ValueChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

        }
        private async void PinPIR_ValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        {
            //simple guard to prevent it from triggering this function again before it's compelted the first time - one photo at a time please
            if (IsInPictureCaptureMode)
                return;
            else
                IsInPictureCaptureMode = true;

            // turn off the LED because we're about to take a picture and send to Bot
            LightLED(false);
            try
            {

                StorageFile picture = await TakePicture();

                if (picture != null)
                    UploadPictureToBot(picture);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                //reset the "IsInPictureMode" singleton guard so the next 
                //PIR movement can come into this method and take a picture
                IsInPictureCaptureMode = false;

                //Turn the LED Status Light on - we're ready for another picture
                LightLED(true);
            }

            return;
        }

        #region Webcam code
        /// <summary>
        /// Initializes the USB Webcam
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void InitilizeWebcam(object sender = null, RoutedEventArgs e = null)
        {
            try
            {
                //initialize the WebCam via MediaCapture object
                MediaCap = new MediaCapture();
                await MediaCap.InitializeAsync();

                // Set callbacks for any possible failure in TakePicture() logic
                MediaCap.Failed += new MediaCaptureFailedEventHandler(MediaCapture_Failed);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            return;
        }

        /// <summary>
        /// Takes a picture from the webcam
        /// </summary>
        /// <returns>StorageFile of image</returns>

        string path = "";
        private DirectLineClient _directLineClient;
        private Conversation _directLineClientConv;

        public async Task<StorageFile> TakePicture()
        {
            try
            {
                //gets a reference to the file we're about to write a picture into
                StorageFile photoFile = await KnownFolders.PicturesLibrary.CreateFileAsync(
                    "RaspPiSecurityPic.jpg", CreationCollisionOption.GenerateUniqueName);
                path = photoFile.Path;
                //use the MediaCapture object to stream captured photo to a file
                ImageEncodingProperties imageProperties = ImageEncodingProperties.CreateJpeg();
                await MediaCap.CapturePhotoToStorageFileAsync(imageProperties, photoFile);
                return photoFile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return null;
            }
        }
        /// <summary>
        /// Callback function for any failures in MediaCapture operations
        /// </summary>
        /// <param name="currentCaptureObject"></param>
        /// <param name="currentFailure"></param>
        private void MediaCapture_Failed(MediaCapture currentCaptureObject, MediaCaptureFailedEventArgs currentFailure)
        {
            Debug.WriteLine(currentFailure.Message);
        }
        #endregion
                
        private void LightLED(bool show = true)
        {
            if (PinLED == null)
                return;

            if (show)
            {
                PinLED.Write(GpioPinValue.Low);
            }
            else
            {
                PinLED.Write(GpioPinValue.High);
            }
        }


        async Task InitializeBotConversations()
        {

            //Initialize Direct Client with secret obtained in the Bot Portal:
            _directLineClient = new DirectLineClient("SecretKey_From_Bot_DrectLine_Channel");
            //Initialize new converstation:
            _directLineClientConv = await _directLineClient.Conversations.NewConversationAsync();
            //Wait for the responses from bot:
            ReadBotMessagesAsync(_directLineClient, _directLineClientConv.ConversationId);

        }
        private ObservableCollection<Microsoft.Bot.Connector.DirectLine.Models.Message> _messagesFromBot;
        private async Task ReadBotMessagesAsync(DirectLineClient _client, string conversationId)
        {
            // You can optionally set watermark -this is last message id seen by bot
            //It is for paging:
            string watermark = null;
            while (true)
            {
                //Get all messages returned by bot:
                var messages = await _directLineClient.Conversations.GetMessagesAsync(conversationId, watermark);
                watermark = messages?.Watermark;
                var messagesFromBotText = from x in messages.Messages
                                          where x.FromProperty == "FacialIdentificationBot"
                                          select x;

                //Iterate through all messages:
                foreach (Microsoft.Bot.Connector.DirectLine.Models.Message message in messagesFromBotText)
                {
                    if (!_messagesFromBot.Contains(message))
                    {
                        _messagesFromBot.Add(message);
                        SendBotMessageToIoTHub(message);
                    }
                }
            }

        }

        public async Task SendBotMessageToIoTHub(Microsoft.Bot.Connector.DirectLine.Models.Message message)
        {
            var deviceClient = DeviceClient.CreateFromConnectionString("Replace_Connection_String_From_Device_Registration_Step");
            var stringContent = JsonConvert.SerializeObject(message);
            var jsonStringInBytes = new Microsoft.Azure.Devices.Client.Message(Encoding.ASCII.GetBytes(stringContent));
            Debug.WriteLine("Message: " + stringContent);
            await deviceClient.SendEventAsync(jsonStringInBytes);

        }
        async Task UploadPictureToBot(StorageFile photoFile)
        {
            // Parse the connection string and return a reference to the storage account.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse("DefaultEndpointsProtocol = https; AccountName = ***REPLACE_WITH_YOUR_STORAGE_ACCOUNT_NAME * **; AccountKey = ***REPLACE_WITH_YOUR_STORAGE_ACCOUNT_KEY * ***");
        // Create the blob client.
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            // Retrieve a reference to a container.
            CloudBlobContainer container = blobClient.GetContainerReference("mycontainer");
            // Retrieve reference to a blob named "myblob".
            CloudBlockBlob blockBlob = container.GetBlockBlobReference("myblob");

            // Create or overwrite the "myblob" blob with contents from a local file.
            using (var fileStream = await photoFile.OpenStreamForReadAsync())
            {
                await blockBlob.UploadFromStreamAsync(fileStream);
            }

            //Add blob URL in bot message as attachment like below

            Microsoft.Bot.Connector.DirectLine.Models.Message userMessage = new Microsoft.Bot.Connector.DirectLine.Models.Message
            {
                FromProperty = "RaspberryPi",
                Text = "Captured Photo"
            };
            userMessage.Attachments.Add(new Attachment() { ContentType = "blob", Url = blockBlob.Uri.ToString() });
            await _directLineClient.Conversations.PostMessageAsync(_directLineClientConv.ConversationId, userMessage);
        }


    }

}
