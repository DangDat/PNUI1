using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Kinect;
using System.Windows.Ink;
using NAudio.Wave;
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;



using System;

namespace Assignment1_U3091855_U3096367_U3103731
{
    //Class for audio
    public abstract class WaveProvider32 : IWaveProvider
    {
        private WaveFormat waveFormat;

        public WaveProvider32()
            : this(44100, 1)
        {
        }

        public WaveProvider32(int sampleRate, int channels)
        {
            SetWaveFormat(sampleRate, channels);
        }

        public void SetWaveFormat(int sampleRate, int channels)
        {
            this.waveFormat =
          WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            WaveBuffer waveBuffer = new WaveBuffer(buffer);
            int samplesRequired = count / 4;
            int samplesRead = Read(waveBuffer.FloatBuffer,
    offset / 4, samplesRequired);
            return samplesRead * 4;
        }

        public abstract int Read(float[] buffer, int offset,
    int sampleCount);

        public WaveFormat WaveFormat
        {
            get { return waveFormat; }
        }
    }

    public class SineWaveProvider32 : WaveProvider32
    {
        int sample;

        public SineWaveProvider32()
        {
            Frequency = 1000;
            Amplitude = 0.25f; // let's not hurt our ears           
        }

        public float Frequency { get; set; }
        public float Amplitude { get; set; }

        public override int Read(float[] buffer, int offset,
    int sampleCount)
        {
            int sampleRate = WaveFormat.SampleRate;
            for (int n = 0; n < sampleCount; n++)
            {
                buffer[n + offset] = (float)(Amplitude * Math.Sin((2 * Math.PI * sample * Frequency) / sampleRate));
                sample++;
                if (sample >= sampleRate) sample = 0;
            }
            return sampleCount;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Width of output drawing
        /// </summary>
        private const float RenderWidth = 640.0f;

        /// <summary>
        /// Height of our output drawing
        /// </summary>
        private const float RenderHeight = 480.0f;

        /// <summary>
        /// Thickness of drawn joint lines
        /// </summary>
        private const double JointThickness = 3;

        /// <summary>
        /// Thickness of body center ellipse
        /// </summary>
        private const double BodyCenterThickness = 10;

        /// <summary>
        /// Thickness of clip edge rectangles
        /// </summary>
        private const double ClipBoundsThickness = 10;

        /// <summary>
        /// Brush used to draw skeleton center point
        /// </summary>
        private readonly Brush centerPointBrush = Brushes.Blue;

        /// <summary>
        /// Brush used for drawing joints that are currently tracked
        /// </summary>
        private readonly Brush trackedJointBrush = new SolidColorBrush(Color.FromArgb(255, 68, 192, 68));

        /// <summary>
        /// Brush used for drawing joints that are currently inferred
        /// </summary>        
        private readonly Brush inferredJointBrush = Brushes.Yellow;

        /// <summary>
        /// Pen used for drawing bones that are currently tracked
        /// </summary>
        private readonly Pen trackedBonePen = new Pen(Brushes.Green, 6);

        /// <summary>
        /// Pen used for drawing bones that are currently inferred
        /// </summary>        
        private readonly Pen inferredBonePen = new Pen(Brushes.Gray, 1);

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor sensor;

        /// <summary>
        /// Drawing group for skeleton rendering output
        /// </summary>
        private DrawingGroup drawingGroup;

        /// <summary>
        /// Drawing image that we will display
        /// </summary>
        private DrawingImage imageSource;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        /// 
        ///A collection to keep(X,Y) for handwritings
        private PolyLineSegment handwritings = new PolyLineSegment();
        
        //Audio declare
        private WaveOut waveOut;


        //KinectSensor
        private SpeechRecognitionEngine speechEngine;
        //int depth;
        int currentTrackingId = 0;

        private SolidColorBrush brush = new SolidColorBrush();


        public MainWindow()
        {
            InitializeComponent();
        }

       

        /// <summary>
        /// Draws indicators to show which edges are clipping skeleton data
        /// </summary>
        /// <param name="skeleton">skeleton to draw clipping information for</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private static void RenderClippedEdges(Skeleton skeleton, DrawingContext drawingContext)
        {
            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Bottom))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, RenderHeight - ClipBoundsThickness, RenderWidth, ClipBoundsThickness));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Top))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, RenderWidth, ClipBoundsThickness));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Left))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, ClipBoundsThickness, RenderHeight));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Right))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(RenderWidth - ClipBoundsThickness, 0, ClipBoundsThickness, RenderHeight));
            }
        }

        /// <summary>
        /// Execute startup tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            brush = Brushes.White;
            // Create the drawing group we'll use for drawing
            this.drawingGroup = new DrawingGroup();

            // Create an image source that we can use in our image control
            this.imageSource = new DrawingImage(this.drawingGroup);

            // Display the drawing using our image control
            Image.Source = this.imageSource;

            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit (See components in Toolkit Browser).
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {
                // Turn on the skeleton stream to receive skeleton frames
                this.sensor.SkeletonStream.Enable();

                // Add an event handler to be called whenever there is new color frame data
                this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;

                // Start the sensor!
                try
                {
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
            }

            if (null == this.sensor)
            {
                this.statusBarText.Text = "Connect Kinect and try again";
                return;
            }

            //For audio recogninzer
            RecognizerInfo ri = (from recognizer in SpeechRecognitionEngine.InstalledRecognizers() where "en-US".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase) select recognizer).FirstOrDefault();
            if (ri != null)
            {
                this.speechEngine = new SpeechRecognitionEngine(ri.Id);
                //var fontsizes = new Choices();
                //fontsizes.Add(new SemanticResultValue("SMALL", "Small"));
                //fontsizes.Add(new SemanticResultValue("MEDIUM", "Medium"));
                //fontsizes.Add(new SemanticResultValue("LARGE", "Large"));
                
                //gb.Append(fontsizes);

                var colors = new Choices();
                colors.Add("yellow");
                colors.Add("red");
                colors.Add("pink");
                colors.Add("green");
                //colors.Add("quit");

                //var quit = new Choices();
                //quit.Add("quit");

                var gb = new GrammarBuilder();
                gb.Culture = ri.Culture;
                gb.Append(colors);
                //gb.Append(quit);
   
                var g = new Grammar(gb);
                speechEngine.LoadGrammar(g);

                this.speechEngine.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(speechEngine_SpeechRecognized);
                speechEngine.SetInputToAudioStream(sensor.AudioSource.Start(), new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
                speechEngine.RecognizeAsync(RecognizeMode.Multiple);

                var q = new GrammarBuilder();
                q.Append("quit");
                var quit = new Grammar(q);
                speechEngine.LoadGrammar(quit);
            }

        }

        private void speechEngine_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            const double ConfidenceThreshold = 0.3;
            if (e.Result.Confidence >= ConfidenceThreshold)
            {
                switch (e.Result.Text)
                {
                    //case "Small":
                    //    textBlockResult.FontSize = 36;
                    //    break;
                    //case "Medium":
                    //    textBlockResult.FontSize = 56;
                    //    break;
                    //case "Large":
                    //    textBlockResult.FontSize = 72;
                    //    break;
                    case "yellow":
                        brush = Brushes.Yellow;
                        textBlockResult.Foreground = Brushes.Yellow;
                        break;
                    case "red":
                        brush = Brushes.Red;
                        textBlockResult.Foreground = Brushes.Red;
                        break;
                    case "pink":
                        brush = Brushes.Pink;
                        textBlockResult.Foreground = Brushes.Pink;
                        break;
                    case "green":
                        brush = Brushes.Green;
                        textBlockResult.Foreground = Brushes.Green;
                        break;
                    case "quit":
                        this.Close();
                        break;
                }
            }
        }
            
            
        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (null != this.sensor)
            {
                this.sensor.Stop();
                this.sensor = null;
            }
            if (this.speechEngine != null)
            {
                this.speechEngine.SpeechRecognized -= new EventHandler<SpeechRecognizedEventArgs>(speechEngine_SpeechRecognized);
                this.speechEngine.RecognizeAsyncStop();
            }
        }

        /// <summary>
        /// Event handler for Kinect sensor's SkeletonFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            Skeleton[] skeletons = new Skeleton[0];

            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    skeletonFrame.CopySkeletonDataTo(skeletons);
                }
            }

            Skeleton skeleton = null;


            // Case 1: you force the SkeletonStream to track only the first 
            // skeleton identified, ignore remaining skeletons. You save 
            // skeleton.TrackingId in a field for handling subsequent frame 
            // events
            if (currentTrackingId == 0)
            {
                skeleton = (from skele in skeletons
                            where skele.TrackingState ==
    SkeletonTrackingState.Tracked
                            select skele).FirstOrDefault();
                if (skeleton != null)
                {
                    currentTrackingId = skeleton.TrackingId;
                    sensor.SkeletonStream.AppChoosesSkeletons = true;
                    sensor.SkeletonStream.ChooseSkeletons(
skeleton.TrackingId);
                    return; // skip here to the next frame
                }
            }

            // Case 2: In a subsequent frame, you find that the skeleton 
            // with the currentTrackingId is no longer present, that means 
            // the person is gone. If you want to automatically find 
            // another skeleton to track, set AppChoosesSkeletons back to 
            // false, which will return the skeleton tracker to auto-detect 
            // mode
            if (currentTrackingId != 0)
            {
                skeleton = (from skele in skeletons
                            where skele.TrackingState ==
SkeletonTrackingState.Tracked &&
skele.TrackingId == currentTrackingId
                            select skele).FirstOrDefault();
                if (skeleton == null)
                {
                    sensor.SkeletonStream.AppChoosesSkeletons = false;
                    currentTrackingId = 0;
                    return; // skip here to the next frame
                }
            }

            using (DrawingContext dc = this.drawingGroup.Open())
            {
                // Draw a transparent background to set the render size
                dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));

                if (skeletons.Length != 0)
                {
                    foreach (Skeleton skel in skeletons)
                    {
                        RenderClippedEdges(skel, dc);

                        if (skel.TrackingState == SkeletonTrackingState.Tracked)
                        {
                            this.DrawBonesAndJoints(skel, dc);
                            //Drawing
                            //this.DrawHandwriting(skel, dc);
                            //Drawing with color
                            this.DrawColorfulHandwriting(skel, dc);
                        }
                        else if (skel.TrackingState == SkeletonTrackingState.PositionOnly)
                        {
                            dc.DrawEllipse(
                            this.centerPointBrush,
                            null,
                            this.SkeletonPointToScreen(skel.Position),
                            BodyCenterThickness,
                            BodyCenterThickness);
                        }
                    }
                }
    


                // prevent drawing outside of our render area
                this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
            }
        }

        /// <summary>
        /// Draws a skeleton's bones and joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void DrawBonesAndJoints(Skeleton skeleton, DrawingContext drawingContext)
        {
            // Render Torso
            this.DrawBone(skeleton, drawingContext, JointType.Head, JointType.ShoulderCenter);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderRight);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.Spine);
            this.DrawBone(skeleton, drawingContext, JointType.Spine, JointType.HipCenter);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipLeft);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipRight);

            // Left Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderLeft, JointType.ElbowLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowLeft, JointType.WristLeft);
            this.DrawBone(skeleton, drawingContext, JointType.WristLeft, JointType.HandLeft);

            // Right Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderRight, JointType.ElbowRight);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowRight, JointType.WristRight);
            this.DrawBone(skeleton, drawingContext, JointType.WristRight, JointType.HandRight);

            // Left Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipLeft, JointType.KneeLeft);
            this.DrawBone(skeleton, drawingContext, JointType.KneeLeft, JointType.AnkleLeft);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleLeft, JointType.FootLeft);

            // Right Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipRight, JointType.KneeRight);
            this.DrawBone(skeleton, drawingContext, JointType.KneeRight, JointType.AnkleRight);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleRight, JointType.FootRight);

            // Render Joints
            foreach (Joint joint in skeleton.Joints)
            {
                Brush drawBrush = null;

                if (joint.TrackingState == JointTrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;
                }
                else if (joint.TrackingState == JointTrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;
                }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, this.SkeletonPointToScreen(joint.Position), JointThickness, JointThickness);
                }
            }


        }

        /// <summary>
        /// Maps a SkeletonPoint to lie within our render space and converts to Point
        /// </summary>
        /// <param name="skelpoint">point to map</param>
        /// <returns>mapped point</returns>
        private Point SkeletonPointToScreen(SkeletonPoint skelpoint)
        {
            // Convert point to depth space.  
            // We are not using depth directly, but we do want the points in our 640x480 output resolution.
            DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }

        ///To get the depth value
        private int SkeletonDepthToScreen(SkeletonPoint skelpoint)
        {
            //Get depth value
            DepthImagePoint depthPoint = this.sensor.MapSkeletonPointToDepth(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return depthPoint.Depth;
        }

        /// <summary>
        /// Draws a bone line between two joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw bones from</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        /// <param name="jointType0">joint to start drawing from</param>
        /// <param name="jointType1">joint to end drawing at</param>
        private void DrawBone(Skeleton skeleton, DrawingContext drawingContext, JointType jointType0, JointType jointType1)
        {
            Joint joint0 = skeleton.Joints[jointType0];
            Joint joint1 = skeleton.Joints[jointType1];

            // If we can't find either of these joints, exit
            if (joint0.TrackingState == JointTrackingState.NotTracked ||
                joint1.TrackingState == JointTrackingState.NotTracked)
            {
                return;
            }

            // Don't draw if both points are inferred
            if (joint0.TrackingState == JointTrackingState.Inferred &&
                joint1.TrackingState == JointTrackingState.Inferred)
            {
                return;
            }

            // We assume all drawn bones are inferred unless BOTH joints are tracked
            Pen drawPen = this.inferredBonePen;
            if (joint0.TrackingState == JointTrackingState.Tracked && joint1.TrackingState == JointTrackingState.Tracked)
            {
                drawPen = this.trackedBonePen;
            }

            drawingContext.DrawLine(drawPen, this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position));
            //Print coordinate
            string xy = "";
            Point pt = this.SkeletonPointToScreen(joint0.Position);
            xy = "(" + pt.X.ToString() + ", " + pt.Y.ToString() + ")";
            FormattedText formattedText = new FormattedText(
                xy,
                System.Globalization.CultureInfo.GetCultureInfo("en-us"),
                FlowDirection.LeftToRight,
                new Typeface("Verdana"),
                12,
                Brushes.White);
            drawingContext.DrawText(formattedText, pt);

        }

        /// <summary>
        /// Handles the checking or unchecking of the seated mode combo box
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void CheckBoxSeatedModeChanged(object sender, RoutedEventArgs e)
        {
            if (null != this.sensor)
            {
                if (this.checkBoxSeatedMode.IsChecked.GetValueOrDefault())
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                }
                else
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
                }
            }
        }

        ///Draw handwritings
        //private void DrawHandwriting(Skeleton skeleton, DrawingContext drawingContext)
        //{

        //    Joint handRightJoint = skeleton.Joints[JointType.HandRight];
        //    Point handRightPoint = SkeletonPointToScreen(handRightJoint.Position);

        //    Joint handLeftJoint = skeleton.Joints[JointType.HandLeft];
        //    Point handLeftPoint = SkeletonPointToScreen(handLeftJoint.Position);


        //    if (handLeftPoint.Y < handRightPoint.Y)
        //    {
        //        if (!handwritings.Points.Contains(handRightPoint))
        //        {
        //            handwritings.Points.Add(handRightPoint);
        //        }
        //        for (int i = 0; i < handwritings.Points.Count - 1; i++)
        //        {
        //            drawingContext.DrawLine(new Pen(Brushes.Yellow, 3), handwritings.Points[i], handwritings.Points[i + 1]);
        //        }
        //    }
        //    else
        //        handwritings.Points.Clear();  // To clear the old painting

        //    if (handwritings.Points.Count > 0)
        //    {
        //        StylusPointCollection spc = new StylusPointCollection();
        //        for (int i = 0; i < handwritings.Points.Count; i++)
        //        {
        //            spc.Add(new StylusPoint(handwritings.Points[i].X, handwritings.Points[i].Y));
        //        }

        //        Stroke s = new Stroke(spc);
        //        StrokeCollection sc = new StrokeCollection(new Stroke[] { s });

        //        InkAnalyzer inkAnalyzer = new InkAnalyzer();
        //        inkAnalyzer.AddStrokes(sc);
        //        AnalysisStatus status = inkAnalyzer.Analyze();
        //        if (status.Successful)
        //        {
        //            textBlockResult.Text = inkAnalyzer.GetRecognizedString();
        //            sc.Clear();
        //        }
        //    }
        //}

        private void DrawColorfulHandwriting(Skeleton skeleton, DrawingContext drawingContext)
        {

            Joint handRightJoint = skeleton.Joints[JointType.HandRight];
            Point handRightPoint = SkeletonPointToScreen(handRightJoint.Position);

            Joint handLeftJoint = skeleton.Joints[JointType.HandLeft];
            Point handLeftPoint = SkeletonPointToScreen(handLeftJoint.Position);

            int depth = SkeletonDepthToScreen(handRightJoint.Position);

            //SolidColorBrush brush = new SolidColorBrush();
            int fontsize;
            if (depth < 1500)
            {
                //brush = Brushes.White;
                fontsize = 36;
            }
            else if (depth < 2000)
            {

                //brush = Brushes.Pink;
                fontsize = 56;
            }
            else
            {
                //brush = Brushes.Red;
                fontsize = 72;
            }

            if (handLeftPoint.Y < handRightPoint.Y)
            {
                if (!handwritings.Points.Contains(handRightPoint))
                {
                    handwritings.Points.Add(handRightPoint);
                }
                for (int i = 0; i < handwritings.Points.Count - 1; i++)
                {
                    drawingContext.DrawLine(new Pen(brush, 3), handwritings.Points[i], handwritings.Points[i + 1]);
                }
                //StartSineWave(depth);
            }
            else
            {
                StopSineWave();
                handwritings.Points.Clear();  // To clear the old painting
            }
            if (handwritings.Points.Count > 0)
            {
                StylusPointCollection spc = new StylusPointCollection();
                for (int i = 0; i < handwritings.Points.Count; i++)
                {
                    spc.Add(new StylusPoint(handwritings.Points[i].X, handwritings.Points[i].Y));
                }

                Stroke s = new Stroke(spc);
                StrokeCollection sc = new StrokeCollection(new Stroke[] { s });

                InkAnalyzer inkAnalyzer = new InkAnalyzer();
                inkAnalyzer.AddStrokes(sc);
                AnalysisStatus status = inkAnalyzer.Analyze();
                if (status.Successful)
                {
                    textBlockResult.Text = inkAnalyzer.GetRecognizedString();
                    textBlockResult.FontSize = fontsize;
                    sc.Clear();
                }
            }
        }

        //Method for audio start

        private void StartSineWave(int depth)
        {
            //Joint handRightJoint = skeleton.Joints[JointType.HandRight];
            //Point handRightPoint = SkeletonPointToScreen(handRightJoint.Position);

            //int depth = SkeletonDepthToScreen(handRightJoint.Position);
            if (waveOut == null)
            {
                var sineWaveProvider = new SineWaveProvider32();
                sineWaveProvider.SetWaveFormat(16000, 1); //16kHz mono
                sineWaveProvider.Frequency = depth;
                sineWaveProvider.Amplitude = 0.25f;
                waveOut = new WaveOut();
                waveOut.Init(sineWaveProvider);
                waveOut.Play();
            }
        }

        //Method for audio stop
        private void StopSineWave()
        {
            if (waveOut != null)
            {
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
            }

        }
        
    }
}
