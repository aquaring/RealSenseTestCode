using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RealSenseHandTracking
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// クラスメンバ変数
        /// </summary>
        // RealSenseSDKのセッション
        private PXCMSession m_Session = null;

        // RealSenseSDKのManager
        private PXCMSenseManager m_Cm = null;

        // HandDataのインスタンス
        PXCMHandData m_HandData = null;

        // 座標変換のためのProjectionインスタンス
        PXCMProjection m_Projection = null;

        // カメラ画像取得を繰り返し処理するためのTask
        private Task m_CameraCaptureTask = null;

        // タスク続行フラグ
        private bool m_TaskContinueFlg = true;

        // Color,Depth,Ir画像書き込み用のWriteableBitmap
        private WriteableBitmap m_ColorWBitmap;
        private WriteableBitmap m_HandSegmentWBitmap;
        private WriteableBitmap m_HandJointWBitmap;

        // 現在のジェスチャ名
        private string m_CurrGesture = "";

        /// <summary>
        /// コンストラクタ
        /// 
        /// カメラ画像表示用のWritableBitmapを準備してImageに割り当て
        /// カメラ制御用のオブジェクトを取得して
        /// Colorカメラストリームを有効にしてカメラのフレーム取得を開始する
        /// カメラキャプチャーするためのTaskを準備して開始
        /// </summary>
        public MainWindow()
        {
            // 画面コンポーネントを初期化
            InitializeComponent();

            // ジェスチャー画像表示用Imageを非表示にする
            HiddenGestureImage();

            // カメラ画像書き込み用のWriteableBitmapを準備してImageコントローラーにセット
            m_ColorWBitmap = new WriteableBitmap(1920, 1080, 96.0, 96.0, PixelFormats.Bgr32, null);
            ColorCameraImage.Source = m_ColorWBitmap;
            m_HandSegmentWBitmap = new WriteableBitmap(640, 480, 96.0, 96.0, PixelFormats.Gray8, null);
            HandSegmentImage.Source = m_HandSegmentWBitmap;
            m_HandJointWBitmap = new WriteableBitmap(640, 480, 96.0, 96.0, PixelFormats.Bgra32, null);
            HandJointtImage.Source = m_HandJointWBitmap;

            // カメラ制御のオブジェクトを取得
            m_Session = PXCMSession.CreateInstance();
            m_Cm = m_Session.CreateSenseManager();

            // Color,Depth,Irストリームを有効にする
            m_Cm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, 1920, 1080, 30);
            m_Cm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, 640, 480, 30);
            m_Cm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_IR, 640, 480, 30);

            // Hand Trackingを有効化と設定
            m_Cm.EnableHand();
            PXCMHandModule handModule = m_Cm.QueryHand();
            PXCMHandConfiguration handConfig = handModule.CreateActiveConfiguration();
            handConfig.SetTrackingMode(PXCMHandData.TrackingModeType.TRACKING_MODE_FULL_HAND);  // FULL_HANDモード
            handConfig.EnableSegmentationImage(true);   // SegmentationImageの有効化
            handConfig.EnableAllGestures();                 // すべてのジェスチャーを補足
            handConfig.SubscribeGesture(OnFiredGesture);    // ジェスチャー発生時のコールバック関数をセット

            handConfig.ApplyChanges();

            // HandDataのインスタンスを作成
            m_HandData = handModule.CreateOutput();

            // カメラのフレーム取得開始
            pxcmStatus initState = m_Cm.Init();
            if (initState < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                // エラー発生
                MessageBox.Show(initState + "\nカメラ初期化に失敗しました。");
                return;
            }

            // カメラ取得画像をミラーモードにする
            m_Cm.captureManager.device.SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);

            // 座標変換のためのProjectionインスタンスを取得
            m_Projection = m_Cm.QueryCaptureManager().QueryDevice().CreateProjection();


            // カメラキャプチャーをするためのタスクを準備して起動
            m_CameraCaptureTask = new Task(() => CaptureCameraProcess());
            m_CameraCaptureTask.Start();
        }

        /// <summary>
        /// カメラキャプチャーの処理
        /// 　Taskで実行し、Waitをはさみながら常に別スレッドで実行する
        /// </summary>
        private void CaptureCameraProcess()
        {
            while (m_TaskContinueFlg)
            {
                // フレームを取得してサンプリング
                pxcmStatus statusFrame = m_Cm.AcquireFrame(true);
                PXCMCapture.Sample sample = m_Cm.QuerySample();
                
                // ColorサンプリングからImageDataを読み込んで取得
                PXCMImage.ImageData colorImageData = null;
                pxcmStatus sts = sample.color.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out colorImageData);
                if (sts == pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    // ImageDataが取得できたらWritableBitmapに書き込むためにバイト配列に変換する
                    int length = colorImageData.pitches[0] * sample.color.info.height;
                    Byte[] buffer = colorImageData.ToByteArray(0, length);

                    // ストライドと描画領域を取得
                    int wbStride = colorImageData.pitches[0];
                    Int32Rect wbRect = new Int32Rect(0, 0, sample.color.info.width, sample.color.info.height);

                    // Colorサンプリングのアクセスを終了する
                    sample.color.ReleaseAccess(colorImageData);

                    // フレームデータをビット配列にしてWriteableBitmapに書き込む
                    m_ColorWBitmap.Dispatcher.BeginInvoke
                    (
                        new Action(() =>
                        {
                            m_ColorWBitmap.WritePixels(wbRect, buffer, wbStride, 0);
                        }
                    ));
                }

                // HandTrackingでHandDataを更新 // TODO:Callback Functions方式もできるので試してみる
                m_HandData.Update();

                // HandTrackingしたデータを表示
                DisplayHandTrackingData(m_HandData);

                // フレームを解放
                m_Cm.ReleaseFrame();

                // 少しWaitする
                Thread.Sleep(30);
            }

            // -----------
            //  終了処理
            // -----------
            // カメラの終了処理
            m_Projection.Dispose();
            m_HandData.Dispose();
            m_Cm.Close();
            m_Cm.Dispose();
            m_Session.Dispose();

            // Windowの終了処理　画面処理のスレッドで実施するようにInvokeする
            MainWin.Dispatcher.BeginInvoke
            (
                new Action(() =>
                {
                    App.Current.Shutdown();
                }
             ));
        }

        /// <summary>
        /// HandTrackingで取得したHandDataを受け取り
        /// 画面にHandDataの情報を表示する
        /// </summary>
        private void DisplayHandTrackingData(PXCMHandData handData)
        {
            // SegmentationImageの情報格納用の変数
            int segWbStride = 0;
            Int32Rect segWbRect = new Int32Rect(0, 0, 0, 0);
            Byte[] segImageBuffer = null;

            // HandJointの情報格納用の変数
            List<List<PXCMHandData.JointData>> handJointsList = new List<List<PXCMHandData.JointData>>();

            for (int handIndex = 0; handIndex < handData.QueryNumberOfHands(); handIndex++)
            {
                // IHandDataを取得
                PXCMHandData.IHand iHandData;
                if (handData.QueryHandData(PXCMHandData.AccessOrderType.ACCESS_ORDER_BY_TIME, handIndex, out iHandData) == pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    // SegmentationImageを取得
                    PXCMImage image;
                    iHandData.QuerySegmentationImage(out image);    // 取得出来る画像は8bitGrayスケール画像 手の部分が0xff(白) 背景が0x00(黒)

                    // Imageから画像データを取得
                    PXCMImage.ImageData data = null ;
                    pxcmStatus sts = image.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_Y8, out data);
                    if (sts == pxcmStatus.PXCM_STATUS_NO_ERROR)
                    {
                        // ImageDataが取得できたらWritableBitmapに書き込むためにバイト配列に変換する
                        int length = data.pitches[0] * image.info.height;
                        Byte[] tmpBuffer = data.ToByteArray(0, length);

                        // ストライドと描画領域を取得
                        segWbStride = data.pitches[0];
                        segWbRect = new Int32Rect(0, 0, image.info.width, image.info.height);

                        // Imageデータのアクセスを終了する
                        image.ReleaseAccess(data);

                        // HandSegmentationImageは複数ある可能性があるためすでにバイト配列を取得ずみの場合は重ね合わせる
                        if (segImageBuffer == null)
                        {
                            // まだない場合は、そのまま使用する
                            Array.Resize(ref segImageBuffer, tmpBuffer.Length);
                            tmpBuffer.CopyTo(segImageBuffer, 0);

                        }
                        else
                        {
                            // 既にひとつの手の情報がある場合は手の白部分(0xff)のみ重ね合わせる
                            for (int i=0; i<segImageBuffer.Length; i++)
                            {
                                segImageBuffer[i] = (byte)(segImageBuffer[i] | tmpBuffer[i]);
                            }
                        }
                    }

                    // TODO:後で取得してみる
                    //iHandData.QueryBoundingBoxImage   // 手の領域
                    //iHandData.QueryMassCenterImage    // 2D Image coordinatesでの手の中心座標
                    //iHandData.QueryMassCenterWorld    // 3D World Coordinatesでの手の中心座標
                    //iHandData.QueryExtremityPoint // TODO:Extremitiesモードで取得してみる

                    // 1つの手のJointを入れるListを生成
                    List<PXCMHandData.JointData> jointList = new List<PXCMHandData.JointData>();
                    
                    // 手のJoint座標を取得してListに格納
                    for (int jointIndex = 0; jointIndex < Enum.GetNames(typeof(PXCMHandData.JointType)).Length; jointIndex++)
                    {
                        // 手の1つのJoint座標を取得
                        PXCMHandData.JointData jointData;
                        iHandData.QueryTrackedJoint((PXCMHandData.JointType)jointIndex, out jointData);

                        jointList.Add(jointData);
                    }

                    // 作成した1つの手のJoint座標リストをListに格納
                    handJointsList.Add(jointList);
                }
            }

            // SegmentationImageデータをバイト配列にしたものをWriteableBitmapに書き込む
            if (segImageBuffer != null)
            {
                m_HandSegmentWBitmap.Dispatcher.BeginInvoke
                (
                    new Action(() =>
                    {
                        m_HandSegmentWBitmap.WritePixels(segWbRect, segImageBuffer, segWbStride, 0);
                    }
                ));
            }

            // HandJointの座標を画面に表示
            if (handJointsList.Count > 0)
            {
                m_ColorWBitmap.Dispatcher.BeginInvoke
                (
                    new Action(() =>
                    {
                        foreach (List<PXCMHandData.JointData> jointList in handJointsList)
                        {
                            foreach (PXCMHandData.JointData joint in jointList)
                            {

                                PXCMPoint3DF32[] depthPoint = new PXCMPoint3DF32[1];
                                
                                depthPoint[0].x = joint.positionImage.x;
                                depthPoint[0].y = joint.positionImage.y;
                                depthPoint[0].z = joint.positionWorld.z * 1000; // mmとpixcelを合わす

                                PXCMPointF32[] colorPoint = new PXCMPointF32[1];
                                pxcmStatus status = m_Projection.MapDepthToColor(depthPoint, colorPoint);

                                // 指の位置を描画                                
                                m_ColorWBitmap.FillEllipseCentered((int)colorPoint[0].x,
                                                                   (int)colorPoint[0].y,
                                                                   10, 10, Colors.YellowGreen);
                            }
                        }
                        
                    }
                ));
            }

            m_HandJointWBitmap.Dispatcher.BeginInvoke
            (
                new Action(() =>
                {
                    m_HandJointWBitmap.Clear();

                    foreach (List<PXCMHandData.JointData> jointList in handJointsList)
                    {
                        foreach (PXCMHandData.JointData joint in jointList)
                        {
                            m_HandJointWBitmap.FillEllipse(
                                               (int)joint.positionImage.x, (int)joint.positionImage.y,
                                               (int)joint.positionImage.x + 6, (int)joint.positionImage.y + 6, Colors.YellowGreen);
                        }
                    }
                }
            ));
            

        }

        /// <summary>
        /// Windowを閉じるときに発生するイベント
        /// 　Task続行フラグを下してTaskを終了させる
        /// 　Windowのクローズ処理はキャンセルする
        /// </summary>
        private void MainWin_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Taskを終了させるために続行フラグを下す
            m_TaskContinueFlg = false;

            // Windowクローズ処理をキャンセル(タスクの終了でアプリを落とすため)
            e.Cancel = true;
        }

        /// <summary>
        /// ジェスチャーを検知した時の処理
        /// </summary>
        void OnFiredGesture(PXCMHandData.GestureData data)
        {
            // ジェスチャー名と画像の表示
            GestureLabel.Dispatcher.BeginInvoke
            (
                new Action(() =>
                {
                    // ジェスチャー名の表示
                    GestureLabel.Content = data.name;

                    // 画像の表示
                    if (m_CurrGesture != data.name)
                    {
                        HiddenGestureImage();

                        Image targetImage = (Image)MainGrid.FindName(data.name + "GestureImage");
                        targetImage.Visibility = Visibility.Visible;
                    }

                    // 今のジェスチャー名を保存
                    m_CurrGesture = data.name;
                }
            ));


            // ジェスチャーごとの処理 今は何もしていない
            if (data.name.CompareTo("click") == 0)
            {

            }
            else if (data.name.CompareTo("fist") == 0)
            {
                
            }
            else if (data.name.CompareTo("full_pinch") == 0)
            {
                
            }
            else if (data.name.CompareTo("spreadfingers") == 0)
            {
                
            }
            else if (data.name.CompareTo("swipe_down") == 0)
            {
                
            }
            else if (data.name.CompareTo("swipe_left") == 0)
            {
                
            }
            else if (data.name.CompareTo("swipe_right") == 0)
            {
                
            }
            else if (data.name.CompareTo("swipe_up") == 0)
            {
                
            }
            else if (data.name.CompareTo("tap") == 0)
            {
                
            }
            else if (data.name.CompareTo("thumb_down") == 0)
            {
                
            }
            else if (data.name.CompareTo("thumb_up") == 0)
            {
                
            }
            else if (data.name.CompareTo("two_fingers_pinch_open") == 0)
            {
                
            }
            else if (data.name.CompareTo("v_sign") == 0)
            {
                
            }
            else if (data.name.CompareTo("wave") == 0)
            {
                
            }
        }

        /// <summary>
        /// ジェスチャー表示用Imageを全て隠す
        /// </summary>
        private void HiddenGestureImage()
        {
            clickGestureImage.Visibility = Visibility.Hidden;
            fistGestureImage.Visibility = Visibility.Hidden;
            full_pinchGestureImage.Visibility = Visibility.Hidden;
            spreadfingersGestureImage.Visibility = Visibility.Hidden;
            swipe_downGestureImage.Visibility = Visibility.Hidden;
            swipe_leftGestureImage.Visibility = Visibility.Hidden;
            swipe_rightGestureImage.Visibility = Visibility.Hidden;
            swipe_upGestureImage.Visibility = Visibility.Hidden;
            tapGestureImage.Visibility = Visibility.Hidden;
            thumb_downGestureImage.Visibility = Visibility.Hidden;
            thumb_upGestureImage.Visibility = Visibility.Hidden;
            two_fingers_pinch_openGestureImage.Visibility = Visibility.Hidden;
            v_signGestureImage.Visibility = Visibility.Hidden;
            waveGestureImage.Visibility = Visibility.Hidden;
        }
    }
}