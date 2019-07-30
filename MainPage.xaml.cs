using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

using Windows.UI.Xaml.Controls;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using System.Threading.Tasks;
using Windows.Media;
using System.Diagnostics;
using Windows.System.Display;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace FacedetectionUWP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MediaCapture mediaCapture;
        private VideoEncodingProperties videoProperties;

        /// <summary>
        /// Holds the current scenario state value.
        /// </summary>
        private ScenarioState currentState;

        /// <summary>
        /// References a FaceDetector instance.
        /// </summary>
        private FaceDetector faceDetector;

        /// <summary>
        /// Brush for drawing the bounding box around each detected face.
        /// </summary>
        private readonly SolidColorBrush lineBrush = new SolidColorBrush(Windows.UI.Colors.Yellow);

        /// <summary>
        /// Thickness of the face bounding box lines.
        /// </summary>
        private readonly double lineThickness = 2.0;

        /// <summary>
        /// Transparent fill for the bounding box.
        /// </summary>
        private readonly SolidColorBrush fillBrush = new SolidColorBrush(Windows.UI.Colors.Transparent);

        DisplayRequest displayRequest = new DisplayRequest();

        public MainPage()
        {
            this.currentState = ScenarioState.Idle;
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            // The 'await' operation can only be used from within an async method but class constructors
            // cannot be labeled as async, and so we'll initialize FaceDetector here.
            if (this.faceDetector == null)
            {
                this.faceDetector = await FaceDetector.CreateAsync();
            }
        }

        /// <summary>
        /// Values for identifying and controlling scenario states.
        /// </summary>
        private enum ScenarioState
        {
            /// <summary>
            /// Display is blank - default state.
            /// </summary>
            Idle,

            /// <summary>
            /// Webcam is actively engaged and a live video stream is displayed.
            /// </summary>
            Streaming,

            /// <summary>
            /// Snapshot image has been captured and is being displayed along with detected faces; webcam is not active.
            /// </summary>
            Snapshot
        }

        private async Task<bool> StartWebcamStreaming()
        {
            bool successful = true;

            try
            {
                this.mediaCapture = new MediaCapture();

                // For this scenario, we only need Video (not microphone) so specify this in the initializer.
                // NOTE: the appxmanifest only declares "webcam" under capabilities and if this is changed to include
                // microphone (default constructor) you must add "microphone" to the manifest or initialization will fail.
                // MediaCaptureInitializationSettings settings = new MediaCaptureInitializationSettings();
                // settings.StreamingCaptureMode = StreamingCaptureMode.Video;
                await this.mediaCapture.InitializeAsync();
                this.displayRequest.RequestActive();
                this.mediaCapture.CameraStreamStateChanged += this.MediaCapture_CameraStreamStateChanged;

                // Cache the media properties as we'll need them later.
                var deviceController = this.mediaCapture.VideoDeviceController;
                this.videoProperties = deviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

                // Immediately start streaming to our CaptureElement UI.
                // NOTE: CaptureElement's Source must be set before streaming is started.
                this.CamPreview.Source = this.mediaCapture;
                await this.mediaCapture.StartPreviewAsync();
            }
            catch (System.UnauthorizedAccessException)
            {
                // If the user has disabled their webcam this exception is thrown; provide a descriptive message to inform the user of this fact.
                successful = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("&&&&&& media steaming error &&&&&&");
                Debug.WriteLine(ex.ToString());
                successful = false;
            }

            return successful;
        }

        /// <summary>
        /// Handles MediaCapture changes by shutting down streaming and returning to Idle state.
        /// </summary>
        /// <param name="sender">The source of the event, i.e. our MediaCapture object</param>
        /// <param name="args">Event data</param>
        private void MediaCapture_CameraStreamStateChanged(MediaCapture sender, object args)
        {
            // MediaCapture is not Agile and so we cannot invoke it's methods on this caller's thread
            // and instead need to schedule the state change on the UI thread.
            var ignored = this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                ChangeScenarioState(ScenarioState.Idle);
            });
        }

        /// <summary>
        /// Manages the scenario's internal state. Invokes the internal methods and updates the UI according to the
        /// passed in state value. Handles failures and resets the state if necessary.
        /// </summary>
        /// <param name="newState">State to switch to</param>
        private async void ChangeScenarioState(ScenarioState newState)
        {
            switch (newState)
            {
                case ScenarioState.Idle:

                    this.ShutdownWebCam();

                    this.SnapshotCanvas.Background = null;
                    this.SnapshotCanvas.Children.Clear();
                    this.CameraSnapshotButton.IsEnabled = false;
                    this.CameraStreamingButton.Content = "Start Streaming";
                    this.CameraSnapshotButton.Content = "Take Snapshot";
                    this.currentState = newState;
                    break;

                case ScenarioState.Streaming:
                    Debug.WriteLine("*****************");
                    Debug.WriteLine(await this.StartWebcamStreaming());
                    if (!await this.StartWebcamStreaming())
                    {
                        this.ChangeScenarioState(ScenarioState.Idle);
                        break;
                    }

                    this.SnapshotCanvas.Background = null;
                    this.SnapshotCanvas.Children.Clear();
                    this.CameraSnapshotButton.IsEnabled = true;
                    this.CameraStreamingButton.Content = "Stop Streaming";
                    this.CameraSnapshotButton.Content = "Take Snapshot";
                    this.currentState = newState;
                    break;

                case ScenarioState.Snapshot:

                    if (!await this.TakeSnapshotAndFindFaces())
                    {
                        this.ChangeScenarioState(ScenarioState.Idle);
                        break;
                    }

                    this.ShutdownWebCam();

                    this.CameraSnapshotButton.IsEnabled = true;
                    this.CameraStreamingButton.Content = "Start Streaming";
                    this.CameraSnapshotButton.Content = "Clear Display";
                    this.currentState = newState;
                    break;
            }
        }

        /// <summary>
        /// Safely stops webcam streaming (if running) and releases MediaCapture object.
        /// </summary>
        private async void ShutdownWebCam()
        {
            if (this.mediaCapture != null)
            {
                if (this.mediaCapture.CameraStreamState == Windows.Media.Devices.CameraStreamState.Streaming)
                {
                    await this.mediaCapture.StopPreviewAsync();
                }

                this.mediaCapture.Dispose();
            }

            this.CamPreview.Source = null;
            this.mediaCapture = null;
        }

        /// <summary>
        /// Captures a single frame from the running webcam stream and executes the FaceDetector on the image. If successful calls SetupVisualization to display the results.
        /// </summary>
        /// <returns>Async Task object returning true if the capture was successful and false if an exception occurred.</returns>
        private async Task<bool> TakeSnapshotAndFindFaces()
        {
            bool successful = true;

            try
            {
                if (this.currentState != ScenarioState.Streaming)
                {
                    return false;
                }

                WriteableBitmap displaySource = null;
                IList<DetectedFace> faces = null;

                // Create a VideoFrame object specifying the pixel format we want our capture image to be (NV12 bitmap in this case).
                // GetPreviewFrame will convert the native webcam frame into this format.
                const BitmapPixelFormat InputPixelFormat = BitmapPixelFormat.Nv12;
                using (VideoFrame previewFrame = new VideoFrame(InputPixelFormat, (int)this.videoProperties.Width, (int)this.videoProperties.Height))
                {
                    await this.mediaCapture.GetPreviewFrameAsync(previewFrame);

                    // The returned VideoFrame should be in the supported NV12 format but we need to verify this.
                    if (FaceDetector.IsBitmapPixelFormatSupported(previewFrame.SoftwareBitmap.BitmapPixelFormat))
                    {
                        faces = await this.faceDetector.DetectFacesAsync(previewFrame.SoftwareBitmap);
                    }
                    else
                    {
                        // this.rootPage.NotifyUser("PixelFormat '" + InputPixelFormat.ToString() + "' is not supported by FaceDetector", NotifyType.ErrorMessage);
                    }

                    // Create a WritableBitmap for our visualization display; copy the original bitmap pixels to wb's buffer.
                    // Note that WriteableBitmap doesn't support NV12 and we have to convert it to 32-bit BGRA.
                    using (SoftwareBitmap convertedSource = SoftwareBitmap.Convert(previewFrame.SoftwareBitmap, BitmapPixelFormat.Bgra8))
                    {
                        displaySource = new WriteableBitmap(convertedSource.PixelWidth, convertedSource.PixelHeight);
                        convertedSource.CopyToBuffer(displaySource.PixelBuffer);
                    }

                    // Create our display using the available image and face results.
                    this.SetupVisualization(displaySource, faces);
                }
            }
            catch (Exception ex)
            {
                // this.rootPage.NotifyUser(ex.ToString(), NotifyType.ErrorMessage);
                successful = false;
            }

            return successful;
        }

        /// <summary>
        /// Takes the webcam image and FaceDetector results and assembles the visualization onto the Canvas.
        /// </summary>
        /// <param name="displaySource">Bitmap object holding the image we're going to display</param>
        /// <param name="foundFaces">List of detected faces; output from FaceDetector</param>
        private void SetupVisualization(WriteableBitmap displaySource, IList<DetectedFace> foundFaces)
        {
            ImageBrush brush = new ImageBrush();
            brush.ImageSource = displaySource;
            brush.Stretch = Stretch.Fill;
            this.SnapshotCanvas.Background = brush;

            if (foundFaces != null)
            {
                double widthScale = displaySource.PixelWidth / this.SnapshotCanvas.ActualWidth;
                double heightScale = displaySource.PixelHeight / this.SnapshotCanvas.ActualHeight;

                foreach (DetectedFace face in foundFaces)
                {
                    // Create a rectangle element for displaying the face box but since we're using a Canvas
                    // we must scale the rectangles according to the image's actual size.
                    // The original FaceBox values are saved in the Rectangle's Tag field so we can update the
                    // boxes when the Canvas is resized.
                    Rectangle box = new Rectangle();
                    box.Tag = face.FaceBox;
                    box.Width = (uint)(face.FaceBox.Width / widthScale);
                    box.Height = (uint)(face.FaceBox.Height / heightScale);
                    box.Fill = this.fillBrush;
                    box.Stroke = this.lineBrush;
                    box.StrokeThickness = this.lineThickness;
                    box.Margin = new Thickness((uint)(face.FaceBox.X / widthScale), (uint)(face.FaceBox.Y / heightScale), 0, 0);

                    this.SnapshotCanvas.Children.Add(box);
                }
            }

            string message;
            if (foundFaces == null || foundFaces.Count == 0)
            {
                message = "Didn't find any human faces in the image";
            }
            else if (foundFaces.Count == 1)
            {
                message = "Found a human face in the image";
            }
            else
            {
                message = "Found " + foundFaces.Count + " human faces in the image";
            }

            // this.rootPage.NotifyUser(message, NotifyType.StatusMessage);
        }

        /// <summary>
        /// Handles "streaming" button clicks to start/stop webcam streaming.
        /// </summary>
        /// <param name="sender">Button user clicked</param>
        /// <param name="e">Event data</param>
        private void CameraStreamingButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.currentState == ScenarioState.Streaming)
            {
                // this.rootPage.NotifyUser(string.Empty, NotifyType.StatusMessage);
                this.ChangeScenarioState(ScenarioState.Idle);
            }
            else
            {
                // this.rootPage.NotifyUser(string.Empty, NotifyType.StatusMessage);
                this.ChangeScenarioState(ScenarioState.Streaming);
            }
        }

        /// <summary>
        /// Handles "snapshot" button clicks to take a snapshot or clear the current display.
        /// </summary>
        /// <param name="sender">Button user clicked</param>
        /// <param name="e">Event data</param>
        private void CameraSnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.currentState == ScenarioState.Streaming)
            {
                // this.rootPage.NotifyUser(string.Empty, NotifyType.StatusMessage);
                this.ChangeScenarioState(ScenarioState.Snapshot);
            }
            else
            {
                // this.rootPage.NotifyUser(string.Empty, NotifyType.StatusMessage);
                this.ChangeScenarioState(ScenarioState.Idle);
            }
        }

        /// <summary>
        /// Updates any existing face bounding boxes in response to changes in the size of the Canvas.
        /// </summary>
        /// <param name="sender">Canvas whose size has changed</param>
        /// <param name="e">Event data</param>
        private void SnapshotCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                // If the Canvas is resized we must recompute a new scaling factor and
                // apply it to each face box.
                if (this.currentState == ScenarioState.Snapshot && this.SnapshotCanvas.Background != null)
                {
                    WriteableBitmap displaySource = (this.SnapshotCanvas.Background as ImageBrush).ImageSource as WriteableBitmap;

                    double widthScale = displaySource.PixelWidth / this.SnapshotCanvas.ActualWidth;
                    double heightScale = displaySource.PixelHeight / this.SnapshotCanvas.ActualHeight;

                    foreach (var item in this.SnapshotCanvas.Children)
                    {
                        Rectangle box = item as Rectangle;
                        if (box == null)
                        {
                            continue;
                        }

                        // We saved the original size of the face box in the rectangles Tag field.
                        BitmapBounds faceBounds = (BitmapBounds)box.Tag;
                        box.Width = (uint)(faceBounds.Width / widthScale);
                        box.Height = (uint)(faceBounds.Height / heightScale);

                        box.Margin = new Thickness((uint)(faceBounds.X / widthScale), (uint)(faceBounds.Y / heightScale), 0, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                // this.rootPage.NotifyUser(ex.ToString(), NotifyType.ErrorMessage);
            }
        }
    }
}
