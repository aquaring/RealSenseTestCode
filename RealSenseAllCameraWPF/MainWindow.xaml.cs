using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RealSenseAllCameraWPF
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

        // カメラ画像取得を繰り返し処理するためのTask
        private Task m_CameraCaptureTask = null;

        // タスク続行フラグ
        private bool m_TaskContinueFlg = true;

        // Color,Depth,Ir画像書き込み用のWriteableBitmap
        private WriteableBitmap m_ColorWBitmap;
        private WriteableBitmap m_DepthWBitmap;
        private WriteableBitmap m_IrWBitmap;

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

            // カメラ画像書き込み用のWriteableBitmapを準備してImageコントローラーにセット
            m_ColorWBitmap = new WriteableBitmap(1920, 1080, 96.0, 96.0, PixelFormats.Bgr32, null);
            ColorCameraImage.Source = m_ColorWBitmap;
            m_DepthWBitmap = new WriteableBitmap(640, 480, 96.0, 96.0, PixelFormats.Gray16, null);
            DepthCameraImage.Source = m_DepthWBitmap;
            m_IrWBitmap = new WriteableBitmap(640, 480, 96.0, 96.0, PixelFormats.Gray8, null);
            IrCameraImage.Source = m_IrWBitmap;

            // カメラ制御のオブジェクトを取得
            m_Session = PXCMSession.CreateInstance();
            m_Cm = m_Session.CreateSenseManager();

            // Color,Depth,Irストリームを有効にする
            m_Cm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, 1920, 1080, 30);
            m_Cm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, 640, 480, 30);
            m_Cm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_IR, 640, 480, 30);

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

                // DepthサンプリングからImageDataを読み込んで取得
                PXCMImage.ImageData depthImageData = null;
                sts = sample.depth.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_DEPTH_RAW, out depthImageData);
                if (sts == pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    // ImageDataが取得できたらWritableBitmapに書き込むためにバイト配列に変換する
                    int length = depthImageData.pitches[0] * sample.depth.info.height;
                    Byte[] buffer = depthImageData.ToByteArray(0, length);

                    // ストライドと描画領域を取得
                    int wbStride = depthImageData.pitches[0];
                    Int32Rect wbRect = new Int32Rect(0, 0, sample.depth.info.width, sample.depth.info.height);

                    // Colorサンプリングのアクセスを終了する
                    sample.depth.ReleaseAccess(depthImageData);

                    // フレームデータをビット配列にしてWriteableBitmapに書き込む
                    m_DepthWBitmap.Dispatcher.BeginInvoke
                    (
                        new Action(() =>
                        {
                            m_DepthWBitmap.WritePixels(wbRect, buffer, wbStride, 0);
                        }
                    ));
                }

                // IrサンプリングからImageDataを読み込んで取得
                PXCMImage.ImageData irImageData = null;
                sts = sample.ir.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_Y8, out irImageData);
                if (sts == pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    // ImageDataが取得できたらWritableBitmapに書き込むためにバイト配列に変換する
                    int length = irImageData.pitches[0] * sample.ir.info.height;
                    Byte[] buffer = irImageData.ToByteArray(0, length);

                    // ストライドと描画領域を取得
                    int wbStride = irImageData.pitches[0];
                    Int32Rect wbRect = new Int32Rect(0, 0, sample.ir.info.width, sample.ir.info.height);

                    // Colorサンプリングのアクセスを終了する
                    sample.ir.ReleaseAccess(irImageData);

                    // フレームデータをビット配列にしてWriteableBitmapに書き込む
                    m_IrWBitmap.Dispatcher.BeginInvoke
                    (
                        new Action(() =>
                        {
                            m_IrWBitmap.WritePixels(wbRect, buffer, wbStride, 0);
                        }
                    ));
                }

                // フレームを解放
                m_Cm.ReleaseFrame();

                // 少しWaitする
                Thread.Sleep(20);
            }

            // -----------
            //  終了処理
            // -----------
            // カメラの終了処理
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
    }
}
