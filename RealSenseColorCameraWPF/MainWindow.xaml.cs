using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RealSenseColorCameraWPF
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

        // Colorカメラ画像書き込み用のWriteableBitmap
        private WriteableBitmap m_ColorWBitmap;

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

            // カメラ制御のオブジェクトを取得
            m_Session = PXCMSession.CreateInstance();
            m_Cm = m_Session.CreateSenseManager();

            // Colorカメラストリームを有効にする
            m_Cm.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, 1920, 1080, 30);

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
                int wbStride = 0;
                Int32Rect wbRect = new Int32Rect(0, 0, 0, 0);
                Byte[] buffer = null;

                // フレームを取得してサンプリング
                pxcmStatus statusFrame = m_Cm.AcquireFrame(true);
                PXCMCapture.Sample sample = m_Cm.QuerySample();
                PXCMImage.ImageData imageData = null;

                // ColorサンプリングからImageDataを読み込んで取得
                pxcmStatus sts = sample.color.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out imageData);
                if (sts == pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    // ImageDataが取得できたらWritableBitmapに書き込むためにバイト配列に変換する
                    int length = imageData.pitches[0] * sample.color.info.height;
                    buffer = imageData.ToByteArray(0, length);

                    // ストライドと描画領域を取得
                    wbStride = imageData.pitches[0];
                    wbRect = new Int32Rect(0, 0, sample.color.info.width, sample.color.info.height);

                    // Colorサンプリングのアクセスを終了する
                    sample.color.ReleaseAccess(imageData);

                    // フレームデータをビット配列にしてWriteableBitmapに書き込む
                    m_ColorWBitmap.Dispatcher.BeginInvoke
                    (
                        new Action(() =>
                        {
                            m_ColorWBitmap.WritePixels(wbRect, buffer, wbStride, 0);
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

            // Windowクローズ処理をキャンセル
            e.Cancel = true;
        }
    }
}
