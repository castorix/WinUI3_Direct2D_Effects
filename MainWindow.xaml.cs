using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

using Direct2D;
using static Direct2D.D2DTools;
using DXGI;
using GlobalStructures;
using static DXGI.DXGITools;
using static GlobalStructures.GlobalTools;
using WIC;
using static WIC.WICTools;
using System.Reflection;
using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging; // BitmapImage

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUI3_Direct2D_Effects
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("Shlwapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern System.Runtime.InteropServices.ComTypes.IStream SHCreateMemStream(IntPtr pInit, uint cbInit);

        ID2D1Factory m_pD2DFactory = null;
        ID2D1Factory1 m_pD2DFactory1 = null;
        IWICImagingFactory m_pWICImagingFactory = null;
        IWICImagingFactory2 m_pWICImagingFactory2 = null;

        IntPtr m_pD3D11DevicePtr = IntPtr.Zero; // Released in CreateDeviceContext : not used
        ID3D11DeviceContext m_pD3D11DeviceContext = null; // Released in Clean : not used
        IDXGIDevice1 m_pDXGIDevice = null;

        ID2D1Device m_pD2DDevice = null; // Released in CreateDeviceContext
        ID2D1DeviceContext m_pD2DDeviceContext = null; // Released in Clean
        ID2D1DeviceContext3 m_pD2DDeviceContext3 = null;

        ID2D1Bitmap1 m_pD2DTargetBitmap = null;
        IDXGISwapChain1 m_pDXGISwapChain1 = null;
        //ID2D1SolidColorBrush m_pMainBrush = null;
        ID2D1Bitmap m_pD2DBitmap = null;
        ID2D1Bitmap m_pD2DBitmap1 = null;
        ID2D1Bitmap m_pD2DBitmapTransparent1 = null;
        ID2D1Bitmap m_pD2DBitmapTransparent2 = null;
        ID2D1Bitmap m_pD2DBitmapMask = null;
        ID2D1Effect m_pBitmapSourceEffect = null;
        ID2D1Bitmap1 m_pD2DBitmapEffect = null;

        private IntPtr hWndMain = IntPtr.Zero;
        private Microsoft.UI.Windowing.AppWindow _apw;

        System.Collections.ObjectModel.ObservableCollection<ComboBoxItem> effects = new System.Collections.ObjectModel.ObservableCollection<ComboBoxItem>();
        BitmapImage m_bitmapImageEffect = new BitmapImage();
        List<StackPanel> listSP = new List<StackPanel>();
        List<ID2D1Bitmap> listImages = new List<ID2D1Bitmap>();

        [ComImport, Guid("63aad0b8-7c24-40ff-85a8-640d944cc325"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ISwapChainPanelNative
        {
            [PreserveSig]
            HRESULT SetSwapChain(IDXGISwapChain swapChain);
        }

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool QueryPerformanceFrequency(out LARGE_INTEGER lpFrequency);

        private LARGE_INTEGER _liFreq;

        public enum ANIMATION : uint
        {
            ANIMATION_TRANSLATE = 0,
            ANIMATION_CROSSFADE = 1,
            ANIMATION_PERSPECTIVE = 2,
            ANIMATION_BLUR = 3,
            ANIMATION_CROP = 4,
            ANIMATION_BRIGHTNESS = 5,
            ANIMATION_ROTATE = 6,
            ANIMATION_GRIDMASK = 7,
            ANIMATION_CHROMA_KEY = 8,
            ANIMATION_MORPHOLOGY = 9,
            ANIMATION_ZOOM = 10,
            ANIMATION_TURBULENCE = 11,
        }
        private uint _Animation = (uint)ANIMATION.ANIMATION_TRANSLATE;

        public MainWindow()
        {
            this.InitializeComponent();
            hWndMain = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId myWndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWndMain);
            _apw = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(myWndId);
            _apw.Resize(new Windows.Graphics.SizeInt32(1700, 900));
            this.Title = "WinUI 3 - Direct2D Effects";
            Application.Current.Resources["ComboBoxItemForegroundDisabled"] = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
            Application.Current.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Microsoft.UI.Colors.RoyalBlue);

            //borderSCP.Visibility = Visibility.Collapsed;
            tsEffectAnim.IsOn = true;

            this.Closed += MainWindow_Closed;

            _liFreq = new LARGE_INTEGER();
            QueryPerformanceFrequency(out _liFreq);

            m_pWICImagingFactory = (IWICImagingFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(WICTools.CLSID_WICImagingFactory));
            m_pWICImagingFactory2 = (IWICImagingFactory2)m_pWICImagingFactory;
            HRESULT hr = CreateD2D1Factory();
            if (hr == HRESULT.S_OK)
            {
                hr = CreateDeviceContext();
                hr = CreateDeviceResources();
                hr = CreateSwapChain(IntPtr.Zero);
                if (hr == HRESULT.S_OK)
                {
                    hr = ConfigureSwapChain();
                    ISwapChainPanelNative panelNative = WinRT.CastExtensions.As<ISwapChainPanelNative>(scpD2D);
                    hr = panelNative.SetSwapChain(m_pDXGISwapChain1);
                }
                scpD2D.SizeChanged += scpD2D_SizeChanged;
                CompositionTarget.Rendering += CompositionTarget_Rendering;
            }

            effects.Add(new ComboBoxItem() { Content = "Filter", IsEnabled = false });
            effects.Add(new ComboBoxItem() { Content = " Convolve Matrix" });
            effects.Add(new ComboBoxItem() { Content = " Directional Blur" });
            effects.Add(new ComboBoxItem() { Content = " Edge Detection" });
            effects.Add(new ComboBoxItem() { Content = " Gaussian Blur" });
            effects.Add(new ComboBoxItem() { Content = " Morphology" });
            effects.Add(new ComboBoxItem() { Content = "Color", IsEnabled = false });
            effects.Add(new ComboBoxItem() { Content = " Gamma Transfer" });
            effects.Add(new ComboBoxItem() { Content = " Color Matrix" });
            effects.Add(new ComboBoxItem() { Content = " Discrete Transfer" });
            effects.Add(new ComboBoxItem() { Content = " Hue-to-RGB" });
            effects.Add(new ComboBoxItem() { Content = " Hue Rotation" });
            effects.Add(new ComboBoxItem() { Content = " Linear Transfer" });
            effects.Add(new ComboBoxItem() { Content = " RGB-to-Hue" });
            effects.Add(new ComboBoxItem() { Content = " Saturation" });
            effects.Add(new ComboBoxItem() { Content = " Table Transfer" });
            effects.Add(new ComboBoxItem() { Content = " Tint" });
            effects.Add(new ComboBoxItem() { Content = "Lighting and Stylizing", IsEnabled = false });
            effects.Add(new ComboBoxItem() { Content = " Displacement Map" });
            effects.Add(new ComboBoxItem() { Content = " Distant-Diffuse lighting" });
            effects.Add(new ComboBoxItem() { Content = " Distant-Specular lighting" });
            effects.Add(new ComboBoxItem() { Content = " Emboss" });
            effects.Add(new ComboBoxItem() { Content = " Point-Diffuse lighting" });
            effects.Add(new ComboBoxItem() { Content = " Point-Specular lighting" });
            effects.Add(new ComboBoxItem() { Content = " Posterize" });
            effects.Add(new ComboBoxItem() { Content = " Shadow" });
            effects.Add(new ComboBoxItem() { Content = " Spot-Diffuse lighting" });
            effects.Add(new ComboBoxItem() { Content = " Spot-Specular lighting" });
            effects.Add(new ComboBoxItem() { Content = " Turbulence" });
            effects.Add(new ComboBoxItem() { Content = "Photo", IsEnabled = false });
            effects.Add(new ComboBoxItem() { Content = " Brightness" });
            effects.Add(new ComboBoxItem() { Content = " Contrast" });
            effects.Add(new ComboBoxItem() { Content = " Exposure" });
            effects.Add(new ComboBoxItem() { Content = " Grayscale" });
            effects.Add(new ComboBoxItem() { Content = " Highlights and Shadows" });
            effects.Add(new ComboBoxItem() { Content = " Invert" });
            effects.Add(new ComboBoxItem() { Content = " Sepia" });
            effects.Add(new ComboBoxItem() { Content = " Sharpen" });
            effects.Add(new ComboBoxItem() { Content = " Straighten" });
            effects.Add(new ComboBoxItem() { Content = " Temperature and Tint" });
            effects.Add(new ComboBoxItem() { Content = " Vignette" });
            effects.Add(new ComboBoxItem() { Content = "Transform", IsEnabled = false });
            effects.Add(new ComboBoxItem() { Content = " 2D Affine Transform" });
            effects.Add(new ComboBoxItem() { Content = " 3D Transform" });
            effects.Add(new ComboBoxItem() { Content = " Perspective Transform" });
            effects.Add(new ComboBoxItem() { Content = " Atlas" });
            effects.Add(new ComboBoxItem() { Content = " Border" });
            effects.Add(new ComboBoxItem() { Content = " Crop" });
            effects.Add(new ComboBoxItem() { Content = " Scale" });
            effects.Add(new ComboBoxItem() { Content = " Tile" });
            effects.Add(new ComboBoxItem() { Content = "Transparency", IsEnabled = false });
            effects.Add(new ComboBoxItem() { Content = " Chroma-Key" });
            effects.Add(new ComboBoxItem() { Content = " Luminance To Alpha" });
            effects.Add(new ComboBoxItem() { Content = " Opacity" });
            effects.Add(new ComboBoxItem() { Content = "Composition", IsEnabled = false });
            effects.Add(new ComboBoxItem() { Content = " Alpha Mask" });
            effects.Add(new ComboBoxItem() { Content = " Arithmetic Composite" });
            effects.Add(new ComboBoxItem() { Content = " Blend" });
            effects.Add(new ComboBoxItem() { Content = " Composite" });
            effects.Add(new ComboBoxItem() { Content = " Cross-Fade" });

            BuildStackPanelList(this.Content);

            // Gaussian Blur

            List<String> listItemsOGB = cmbOptimizationGaussianBlur.Items
                   .Cast<ComboBoxItem>()
                   .Select(item => item.Content.ToString())
                   .ToList();
            cmbOptimizationGaussianBlur.SelectedIndex = listItemsOGB.FindIndex(s => s.Equals("Balanced"));

            // Directional Blur

            List<String> listItemsODB = cmbOptimizationDirectionalBlur.Items
                 .Cast<ComboBoxItem>()
                 .Select(item => item.Content.ToString())
                 .ToList();
            cmbOptimizationDirectionalBlur.SelectedIndex = listItemsODB.FindIndex(s => s.Equals("Balanced"));

            // Shadow

            List<String> listItemsOS = cmbOptimizationShadow.Items
                 .Cast<ComboBoxItem>()
                 .Select(item => item.Content.ToString())
                 .ToList();
            cmbOptimizationShadow.SelectedIndex = listItemsOS.FindIndex(s => s.Equals("Balanced"));


            // Convolve Matrix

            //float[] aFloatArray = { -1, -1, -1, -1, 9, -1, -1, -1, -1 };
            nb7.Value = nb8.Value = nb9.Value = nb12.Value = nb14.Value = nb17.Value = nb18.Value = nb19.Value = -1;
            nb13.Value = 9;

            List<String> listItemsSMM = cmbScaleModeMatrix.Items
                            .Cast<ComboBoxItem>()
                            .Select(item => item.Content.ToString())
                            .ToList();
            cmbScaleModeMatrix.SelectedIndex = listItemsSMM.FindIndex(s => s.Equals("Linear"));

            nbDivisor.Value = _DivisorMatrix;

            // Color Matrix

            // Blue
            //float[] aFloatArray = { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0 };
            //nbColorMatrix3.Value = nbColorMatrix6.Value = nbColorMatrix9.Value = nbColorMatrix16.Value = 1;

            // Grayscale Matrix

            nbColorMatrix1.Value = nbColorMatrix2.Value = nbColorMatrix3.Value = 0.33;
            nbColorMatrix5.Value = nbColorMatrix6.Value = nbColorMatrix7.Value = 0.33;
            nbColorMatrix9.Value = nbColorMatrix10.Value = nbColorMatrix11.Value = 0.33;
            nbColorMatrix20.Value = 1;

            // Discrete Transfer

            nbDiscreteTransferRed1.Value = 0.0f;
            nbDiscreteTransferRed2.Value = 0.25f;
            nbDiscreteTransferRed3.Value = 0.5f;
            nbDiscreteTransferRed4.Value = 0.75f;
            nbDiscreteTransferRed5.Value = 1.0f;

            nbDiscreteTransferGreen1.Value = 0.0f;
            nbDiscreteTransferGreen2.Value = 0.25f;
            nbDiscreteTransferGreen3.Value = 0.5f;
            nbDiscreteTransferGreen4.Value = 0.75f;
            nbDiscreteTransferGreen5.Value = 1.0f;

            nbDiscreteTransferBlue1.Value = 0.0f;
            nbDiscreteTransferBlue2.Value = 0.5f;
            nbDiscreteTransferBlue3.Value = 1.0f;
            nbDiscreteTransferBlue4.Value = 1.0f;
            nbDiscreteTransferBlue5.Value = 1.0f;

            // Table Transfer

            nbTableTransferRed1.Value = 0.0f;
            nbTableTransferRed2.Value = 0.25f;
            nbTableTransferRed3.Value = 0.5f;
            nbTableTransferRed4.Value = 0.75f;
            nbTableTransferRed5.Value = 1.0f;

            nbTableTransferGreen1.Value = 0.0f;
            nbTableTransferGreen2.Value = 0.25f;
            nbTableTransferGreen3.Value = 0.5f;
            nbTableTransferGreen4.Value = 0.75f;
            nbTableTransferGreen5.Value = 1.0f;

            nbTableTransferBlue1.Value = 0.75f;
            nbTableTransferBlue2.Value = 1.0f;
            nbTableTransferBlue3.Value = 1.0f;
            nbTableTransferBlue4.Value = 1.0f;
            nbTableTransferBlue5.Value = 1.0f;

            // Distant-Diffuse Lighting

            List<String> listItemsDD = cmbScaleModeDistantDiffuse.Items
                           .Cast<ComboBoxItem>()
                           .Select(item => item.Content.ToString())
                           .ToList();
            cmbScaleModeDistantDiffuse.SelectedIndex = listItemsDD.FindIndex(s => s.Equals("Linear"));

            // Distant-Specular Lighting

            List<String> listItemsDS = cmbScaleModeDistantSpecular.Items
                           .Cast<ComboBoxItem>()
                           .Select(item => item.Content.ToString())
                           .ToList();
            cmbScaleModeDistantSpecular.SelectedIndex = listItemsDS.FindIndex(s => s.Equals("Linear"));

            // Point-Diffuse Lighting

            List<String> listItemsPD = cmbScaleModePointDiffuse.Items
                           .Cast<ComboBoxItem>()
                           .Select(item => item.Content.ToString())
                           .ToList();
            cmbScaleModePointDiffuse.SelectedIndex = listItemsPD.FindIndex(s => s.Equals("Linear"));

            // Point-Specular Lighting

            List<String> listItemsPS = cmbScaleModePointSpecular.Items
                           .Cast<ComboBoxItem>()
                           .Select(item => item.Content.ToString())
                           .ToList();
            cmbScaleModePointSpecular.SelectedIndex = listItemsPS.FindIndex(s => s.Equals("Linear"));

            // Spot-Diffuse Lighting

            List<String> listItemsSD = cmbScaleModeSpotDiffuse.Items
                           .Cast<ComboBoxItem>()
                           .Select(item => item.Content.ToString())
                           .ToList();
            cmbScaleModeSpotDiffuse.SelectedIndex = listItemsSD.FindIndex(s => s.Equals("Linear"));

            // Spot-Specular Lighting

            List<String> listItemsSS = cmbScaleModeSpotSpecular.Items
                           .Cast<ComboBoxItem>()
                           .Select(item => item.Content.ToString())
                           .ToList();
            cmbScaleModeSpotSpecular.SelectedIndex = listItemsSS.FindIndex(s => s.Equals("Linear"));

            // Straighten

            List<String> listItemsS = cmbScaleModeStraighten.Items
                           .Cast<ComboBoxItem>()
                           .Select(item => item.Content.ToString())
                           .ToList();
            cmbScaleModeStraighten.SelectedIndex = listItemsS.FindIndex(s => s.Equals("Linear"));

            // 2D Affine Transform

            nbAffineTransform1.Value = 0.9f;
            nbAffineTransform2.Value = -0.1f;

            nbAffineTransform3.Value = 0.1f;
            nbAffineTransform4.Value = 0.9f;

            nbAffineTransform5.Value = 8.0f;
            nbAffineTransform6.Value = 45.0f;

            List<String> listItemsIMAT = cmbInterpolationModeAffineTransform.Items
                        .Cast<ComboBoxItem>()
                        .Select(item => item.Content.ToString())
                        .ToList();
            cmbInterpolationModeAffineTransform.SelectedIndex = listItemsIMAT.FindIndex(s => s.Equals("Linear"));

            // 3D Transform

            //nbTransform1.Value = 0.866f;
            //nbTransform2.Value = 0.25f;
            //nbTransform3.Value = -0.433f;
            //nbTransform4.Value = 0.0f;

            //nbTransform5.Value = 0.0f;
            //nbTransform6.Value = 0.866f;
            //nbTransform7.Value = 0.5f;
            //nbTransform8.Value = 0.0f;

            //nbTransform9.Value = 0.5f;
            //nbTransform10.Value = -0.433f;
            //nbTransform11.Value = 0.75f;
            //nbTransform12.Value = 0.0f;

            //nbTransform13.Value = 0.0f;
            //nbTransform14.Value = 0.0f;
            //nbTransform15.Value = 0.0f;
            //nbTransform16.Value = 1.0f;

            nbTransform1.Value = 0.75f;
            nbTransform2.Value = 0.433f;
            nbTransform3.Value = -0.5f;
            nbTransform4.Value = 0.00125f;

            nbTransform5.Value = -0.216f;
            nbTransform6.Value = 0.875f;
            nbTransform7.Value = 0.433f;
            nbTransform8.Value = -0.001f;

            nbTransform9.Value = 0.625f;
            nbTransform10.Value = -0.216f;
            nbTransform11.Value = 0.75f;
            nbTransform12.Value = -0.0019f;

            nbTransform13.Value = 95.67f;
            nbTransform14.Value = -2.5f;
            nbTransform15.Value = 91.34f;
            nbTransform16.Value = 1.229f;

            List<String> listItemsIMT = cmbInterpolationModeTransform.Items
                      .Cast<ComboBoxItem>()
                      .Select(item => item.Content.ToString())
                      .ToList();
            cmbInterpolationModeTransform.SelectedIndex = listItemsIMT.FindIndex(s => s.Equals("Linear"));

            // Perspective Transform

            nbPerspectiveTransformDepth.Value = 1000;

            List<String> listItemsIMPT = cmbInterpolationModePerspectiveTransform.Items
                       .Cast<ComboBoxItem>()
                       .Select(item => item.Content.ToString())
                       .ToList();
            cmbInterpolationModePerspectiveTransform.SelectedIndex = listItemsIMPT.FindIndex(s => s.Equals("Linear"));

            // Atlas

            nbAtlasInputRect1.Value = 520;
            nbAtlasInputRect2.Value = 10;
            nbAtlasInputRect3.Value = 250;
            nbAtlasInputRect4.Value = 500;

            //nbAtlasInputPaddingRect1.Value = -float.MaxValue;
            //nbAtlasInputPaddingRect2.Value = -float.MaxValue; 
            //nbAtlasInputPaddingRect3.Value = float.MaxValue;
            //nbAtlasInputPaddingRect4.Value = float.MaxValue;

            // Crop

            nbCropRect1.Value = 100;
            nbCropRect2.Value = 100;
            nbCropRect3.Value = 300;
            nbCropRect4.Value = 300;

            // Scale

            List<String> listItemsIMS = cmbInterpolationModeScale.Items
                      .Cast<ComboBoxItem>()
                      .Select(item => item.Content.ToString())
                      .ToList();
            cmbInterpolationModeScale.SelectedIndex = listItemsIMS.FindIndex(s => s.Equals("Linear"));

            // Tile

            nbTileRect1.Value = 0;
            nbTileRect2.Value = 0;
            nbTileRect3.Value = 100;
            nbTileRect4.Value = 100;

            // Arithmetic Composite

            //nbC1.Value = 0.25f;
            //nbC2.Value = 0.5f;
            //nbC3.Value = 0.75f;
            //nbC4.Value = 0;

            nbC1.Value = 0;
            nbC2.Value = 0.5f;
            nbC3.Value = 0.5f;
            nbC4.Value = 0;

            // Blend

            List<String> listItemsBMB = cmbBlendModeBlend.Items
                         .Cast<ComboBoxItem>()
                         .Select(item => item.Content.ToString())
                         .ToList();
            cmbBlendModeBlend.SelectedIndex = listItemsBMB.FindIndex(s => s.Equals("Multiply"));

            // Composite

            List<String> listItemsCMC = cmbCompositeModeComposite.Items
                        .Cast<ComboBoxItem>()
                        .Select(item => item.Content.ToString())
                        .ToList();
            cmbCompositeModeComposite.SelectedIndex = listItemsCMC.FindIndex(s => s.Equals("Source Over"));

            // Turbulence

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            nbTurbulenceSizeX.Value = sizeBitmapF.width;
            nbTurbulenceSizeY.Value = sizeBitmapF.height;

            // For mouse move

            imgEffect.PointerMoved += ImgEffect_PointerMoved;
        }


        public event PropertyChangedEventHandler PropertyChanged;

        // Gaussian Blur
        private float _StandardDeviationGaussianBlur = 3.0f;
        private float StandardDeviationGaussianBlur
        {
            get => _StandardDeviationGaussianBlur;
            set
            {
                _StandardDeviationGaussianBlur = value;
                EffectGaussianBlur();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StandardDeviationGaussianBlur)));
            }
        }
        public double GetStandardDeviationGaussianBlur(float? x) => _StandardDeviationGaussianBlur;
        public float? SetStandardDeviationGaussianBlur(double x) => StandardDeviationGaussianBlur = (float)x;

        private uint _OptimizationGaussianBlur = (uint)D2D1_GAUSSIANBLUR_OPTIMIZATION.D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED;
        private uint _BorderModeGaussianBlur = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;

        // Directional Blur
        private float _StandardDeviationDirectionalBlur = 3.0f;
        private float StandardDeviationDirectionalBlur
        {
            get => _StandardDeviationDirectionalBlur;
            set
            {
                _StandardDeviationDirectionalBlur = value;
                EffectDirectionalBlur();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StandardDeviationDirectionalBlur)));
            }
        }
        public double GetStandardDeviationDirectionalBlur(float? x) => _StandardDeviationDirectionalBlur;
        public float? SetStandardDeviationDirectionalBlur(double x) => StandardDeviationDirectionalBlur = (float)x;

        private float _AngleDirectionalBlur = 0.0f;
        private float AngleDirectionalBlur
        {
            get => _AngleDirectionalBlur;
            set
            {
                _AngleDirectionalBlur = value;
                EffectDirectionalBlur();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AngleDirectionalBlur)));
            }
        }
        public double GetAngleDirectionalBlur(float? x) => _AngleDirectionalBlur;
        public float? SetAngleDirectionalBlur(double x) => AngleDirectionalBlur = (float)x;

        private uint _OptimizationDirectionalBlur = (uint)D2D1_DIRECTIONALBLUR_OPTIMIZATION.D2D1_DIRECTIONALBLUR_OPTIMIZATION_SPEED;
        private uint _BorderModeDirectionalBlur = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;

        // Gamma Transfer
        private float _RedAmplitude = 0.5f;
        private float RedAmplitude
        {
            get => _RedAmplitude;
            set
            {
                _RedAmplitude = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedAmplitude)));
            }
        }
        public double GetRedAmplitude(float? x) => _RedAmplitude;
        public float? SetRedAmplitude(double x) => RedAmplitude = (float)x;

        private float _RedExponent = 0.5f;
        private float RedExponent
        {
            get => _RedExponent;
            set
            {
                _RedExponent = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedExponent)));
            }
        }
        public double GetRedExponent(float? x) => _RedExponent;
        public float? SetRedExponent(double x) => RedExponent = (float)x;

        private float _RedOffset = 0.5f;
        private float RedOffset
        {
            get => _RedOffset;
            set
            {
                _RedOffset = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedOffset)));
            }
        }
        public double GetRedOffset(float? x) => _RedOffset;
        public float? SetRedOffset(double x) => RedOffset = (float)x;

        private float _GreenAmplitude = 0.5f;
        private float GreenAmplitude
        {
            get => _GreenAmplitude;
            set
            {
                _GreenAmplitude = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GreenAmplitude)));
            }
        }
        public double GetGreenAmplitude(float? x) => _GreenAmplitude;
        public float? SetGreenAmplitude(double x) => GreenAmplitude = (float)x;

        private float _GreenExponent = 0.5f;
        private float GreenExponent
        {
            get => _GreenExponent;
            set
            {
                _GreenExponent = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GreenExponent)));
            }
        }
        public double GetGreenExponent(float? x) => _GreenExponent;
        public float? SetGreenExponent(double x) => GreenExponent = (float)x;

        private float _GreenOffset = 0.5f;
        private float GreenOffset
        {
            get => _GreenOffset;
            set
            {
                _GreenOffset = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GreenOffset)));
            }
        }
        public double GetGreenOffset(float? x) => _GreenOffset;
        public float? SetGreenOffset(double x) => GreenOffset = (float)x;

        private float _BlueAmplitude = 0.5f;
        private float BlueAmplitude
        {
            get => _BlueAmplitude;
            set
            {
                _BlueAmplitude = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlueAmplitude)));
            }
        }
        public double GetBlueAmplitude(float? x) => _BlueAmplitude;
        public float? SetBlueAmplitude(double x) => BlueAmplitude = (float)x;

        private float _BlueExponent = 0.5f;
        private float BlueExponent
        {
            get => _BlueExponent;
            set
            {
                _BlueExponent = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlueExponent)));
            }
        }
        public double GetBlueExponent(float? x) => _BlueExponent;
        public float? SetBlueExponent(double x) => BlueExponent = (float)x;

        private float _BlueOffset = 0.5f;
        private float BlueOffset
        {
            get => _BlueOffset;
            set
            {
                _BlueOffset = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlueOffset)));
            }
        }
        public double GetBlueOffset(float? x) => _BlueOffset;
        public float? SetBlueOffset(double x) => BlueOffset = (float)x;

        private float _AlphaAmplitude = 0.5f;
        private float AlphaAmplitude
        {
            get => _AlphaAmplitude;
            set
            {
                _AlphaAmplitude = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlphaAmplitude)));
            }
        }
        public double GetAlphaAmplitude(float? x) => _AlphaAmplitude;
        public float? SetAlphaAmplitude(double x) => AlphaAmplitude = (float)x;

        private float _AlphaExponent = 0.5f;
        private float AlphaExponent
        {
            get => _AlphaExponent;
            set
            {
                _AlphaExponent = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlphaExponent)));
            }
        }
        public double GetAlphaExponent(float? x) => _AlphaExponent;
        public float? SetAlphaExponent(double x) => AlphaExponent = (float)x;

        private float _AlphaOffset = 0.5f;
        private float AlphaOffset
        {
            get => _AlphaOffset;
            set
            {
                _AlphaOffset = value;
                EffectGammaTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlphaOffset)));
            }
        }
        public double GetAlphaOffset(float? x) => _AlphaOffset;
        public float? SetAlphaOffset(double x) => AlphaOffset = (float)x;

        // Convolve Matrix

        private uint _BorderModeMatrix = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
        private uint _ScaleModeMatrix = (uint)D2D1_CONVOLVEMATRIX_SCALE_MODE.D2D1_CONVOLVEMATRIX_SCALE_MODE_LINEAR;
        private float _DivisorMatrix = 1.0f;

        private float _BiasMatrix = 0.0f;
        private float BiasMatrix
        {
            get => _BiasMatrix;
            set
            {
                _BiasMatrix = value;
                EffectConvolveMatrix();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BiasMatrix)));
            }
        }
        public double GetBiasMatrix(float? x) => _BiasMatrix;
        public float? SetBiasMatrix(double x) => BiasMatrix = (float)x;

        // Edge Detection

        private float _StrengthEdgeDetection = 0.5f;
        private float StrengthEdgeDetection
        {
            get => _StrengthEdgeDetection;
            set
            {
                _StrengthEdgeDetection = value;
                EffectEdgeDetection();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StrengthEdgeDetection)));
            }
        }
        public double GetStrengthEdgeDetection(float? x) => _StrengthEdgeDetection;
        public float? SetStrengthEdgeDetection(double x) => StrengthEdgeDetection = (float)x;

        private float _BlurRadiusEdgeDetection = 0.0f;
        private float BlurRadiusEdgeDetection
        {
            get => _BlurRadiusEdgeDetection;
            set
            {
                _BlurRadiusEdgeDetection = value;
                EffectEdgeDetection();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlurRadiusEdgeDetection)));
            }
        }
        public double GetBlurRadiusEdgeDetection(float? x) => _BlurRadiusEdgeDetection;
        public float? SetBlurRadiusEdgeDetection(double x) => BlurRadiusEdgeDetection = (float)x;

        private uint _ModeEdgeDetection = (uint)D2D1_EDGEDETECTION_MODE.D2D1_EDGEDETECTION_MODE_SOBEL;
        //private uint _AlphaModeEdgeDetection = (uint)D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED;

        // Morphology

        private float _WidthMorphology = 1.0f;
        private float WidthMorphology
        {
            get => _WidthMorphology;
            set
            {
                _WidthMorphology = value;
                EffectMorphology();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WidthMorphology)));
            }
        }
        public double GetWidthMorphology(float? x) => _WidthMorphology;
        public float? SetWidthMorphology(double x) => WidthMorphology = (float)x;

        private float _HeightMorphology = 1.0f;
        private float HeightMorphology
        {
            get => _HeightMorphology;
            set
            {
                _HeightMorphology = value;
                EffectMorphology();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeightMorphology)));
            }
        }
        public double GetHeightMorphology(float? x) => _HeightMorphology;
        public float? SetHeightMorphology(double x) => HeightMorphology = (float)x;

        private uint _ModeMorphology = (uint)D2D1_MORPHOLOGY_MODE.D2D1_MORPHOLOGY_MODE_ERODE;

        // Hue-to-RGB

        private uint _InputColorSpaceHueToRGB = (uint)D2D1_HUETORGB_INPUT_COLOR_SPACE.D2D1_HUETORGB_INPUT_COLOR_SPACE_HUE_SATURATION_VALUE;

        // RGB-to-Hue

        private uint _OutputColorSpaceRGBToHue = (uint)D2D1_RGBTOHUE_OUTPUT_COLOR_SPACE.D2D1_RGBTOHUE_OUTPUT_COLOR_SPACE_HUE_SATURATION_VALUE;

        // Hue Rotation

        private float _AngleHueRotation = 0.0f;
        private float AngleHueRotation
        {
            get => _AngleHueRotation;
            set
            {
                _AngleHueRotation = value;
                EffectHueRotation();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AngleHueRotation)));
            }
        }
        public double GetAngleHueRotation(float? x) => _AngleHueRotation;
        public float? SetAngleHueRotation(double x) => AngleHueRotation = (float)x;

        // Linear Transfer

        private float _RedYInterceptLinearTransfer = 0.0f;
        private float RedYInterceptLinearTransfer
        {
            get => _RedYInterceptLinearTransfer;
            set
            {
                _RedYInterceptLinearTransfer = value;
                EffectLinearTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedYInterceptLinearTransfer)));
            }
        }
        public double GetRedYInterceptLinearTransfer(float? x) => _RedYInterceptLinearTransfer;
        public float? SetRedYInterceptLinearTransfer(double x) => RedYInterceptLinearTransfer = (float)x;

        private float _RedSlopeLinearTransfer = 1.0f;
        private float RedSlopeLinearTransfer
        {
            get => _RedSlopeLinearTransfer;
            set
            {
                _RedSlopeLinearTransfer = value;
                EffectLinearTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedSlopeLinearTransfer)));
            }
        }
        public double GetRedSlopeLinearTransfer(float? x) => _RedSlopeLinearTransfer;
        public float? SetRedSlopeLinearTransfer(double x) => RedSlopeLinearTransfer = (float)x;

        private float _GreenYInterceptLinearTransfer = 0.0f;
        private float GreenYInterceptLinearTransfer
        {
            get => _GreenYInterceptLinearTransfer;
            set
            {
                _GreenYInterceptLinearTransfer = value;
                EffectLinearTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GreenYInterceptLinearTransfer)));
            }
        }
        public double GetGreenYInterceptLinearTransfer(float? x) => _GreenYInterceptLinearTransfer;
        public float? SetGreenYInterceptLinearTransfer(double x) => GreenYInterceptLinearTransfer = (float)x;

        private float _GreenSlopeLinearTransfer = 1.0f;
        private float GreenSlopeLinearTransfer
        {
            get => _GreenSlopeLinearTransfer;
            set
            {
                _GreenSlopeLinearTransfer = value;
                EffectLinearTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GreenSlopeLinearTransfer)));
            }
        }
        public double GetGreenSlopeLinearTransfer(float? x) => _GreenSlopeLinearTransfer;
        public float? SetGreenSlopeLinearTransfer(double x) => GreenSlopeLinearTransfer = (float)x;

        private float _BlueYInterceptLinearTransfer = 0.0f;
        private float BlueYInterceptLinearTransfer
        {
            get => _BlueYInterceptLinearTransfer;
            set
            {
                _BlueYInterceptLinearTransfer = value;
                EffectLinearTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlueYInterceptLinearTransfer)));
            }
        }
        public double GetBlueYInterceptLinearTransfer(float? x) => _BlueYInterceptLinearTransfer;
        public float? SetBlueYInterceptLinearTransfer(double x) => BlueYInterceptLinearTransfer = (float)x;

        private float _BlueSlopeLinearTransfer = 1.0f;
        private float BlueSlopeLinearTransfer
        {
            get => _BlueSlopeLinearTransfer;
            set
            {
                _BlueSlopeLinearTransfer = value;
                EffectLinearTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlueSlopeLinearTransfer)));
            }
        }
        public double GetBlueSlopeLinearTransfer(float? x) => _BlueSlopeLinearTransfer;
        public float? SetBlueSlopeLinearTransfer(double x) => BlueSlopeLinearTransfer = (float)x;

        private float _AlphaYInterceptLinearTransfer = 0.0f;
        private float AlphaYInterceptLinearTransfer
        {
            get => _AlphaYInterceptLinearTransfer;
            set
            {
                _AlphaYInterceptLinearTransfer = value;
                EffectLinearTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlphaYInterceptLinearTransfer)));
            }
        }
        public double GetAlphaYInterceptLinearTransfer(float? x) => _AlphaYInterceptLinearTransfer;
        public float? SetAlphaYInterceptLinearTransfer(double x) => AlphaYInterceptLinearTransfer = (float)x;

        private float _AlphaSlopeLinearTransfer = 1.0f;
        private float AlphaSlopeLinearTransfer
        {
            get => _AlphaSlopeLinearTransfer;
            set
            {
                _AlphaSlopeLinearTransfer = value;
                EffectLinearTransfer();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlphaSlopeLinearTransfer)));
            }
        }
        public double GetAlphaSlopeLinearTransfer(float? x) => _AlphaSlopeLinearTransfer;
        public float? SetAlphaSlopeLinearTransfer(double x) => AlphaSlopeLinearTransfer = (float)x;

        // Saturation

        private float _Saturation = 0.5f;
        private float Saturation
        {
            get => _Saturation;
            set
            {
                _Saturation = value;
                EffectSaturation();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Saturation)));
            }
        }
        public double GetSaturation(float? x) => _Saturation;
        public float? SetSaturation(double x) => Saturation = (float)x;

        // Tint
        //private Windows.UI.Color _TintColor = Microsoft.UI.Colors.Red;

        private Windows.UI.Color _TintColor = Microsoft.UI.Colors.Blue;
        private Windows.UI.Color TintColor
        {
            get => _TintColor;
            set
            {
                _TintColor = value;
                EffectTint();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TintColor)));
            }
        }
        public Windows.UI.Color GetTintColor(Windows.UI.Color? x) => _TintColor;
        public Windows.UI.Color? SetTintColor(Windows.UI.Color x) => TintColor = (Windows.UI.Color)x;

        // Distant-diffuse Lighting

        private float _DistantDiffuseLightingAzimuth = 0.0f;
        private float DistantDiffuseLightingAzimuth
        {
            get => _DistantDiffuseLightingAzimuth;
            set
            {
                _DistantDiffuseLightingAzimuth = value;
                EffectDistantDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantDiffuseLightingAzimuth)));
            }
        }
        public double GetDistantDiffuseLightingAzimuth(float? x) => _DistantDiffuseLightingAzimuth;
        public float? SetDistantDiffuseLightingAzimuth(double x) => DistantDiffuseLightingAzimuth = (float)x;

        private float _DistantDiffuseLightingElevation = 0.0f;
        private float DistantDiffuseLightingElevation
        {
            get => _DistantDiffuseLightingElevation;
            set
            {
                _DistantDiffuseLightingElevation = value;
                EffectDistantDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantDiffuseLightingElevation)));
            }
        }
        public double GetDistantDiffuseLightingElevation(float? x) => _DistantDiffuseLightingElevation;
        public float? SetDistantDiffuseLightingElevation(double x) => DistantDiffuseLightingElevation = (float)x;

        private Windows.UI.Color _DistantDiffuseColor = Microsoft.UI.Colors.Blue;
        private Windows.UI.Color DistantDiffuseColor
        {
            get => _DistantDiffuseColor;
            set
            {
                _DistantDiffuseColor = value;
                EffectDistantDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantDiffuseColor)));
            }
        }
        public Windows.UI.Color GetDistantDiffuseColor(Windows.UI.Color? x) => _DistantDiffuseColor;
        public Windows.UI.Color? SetDistantDiffuseColor(Windows.UI.Color x) => DistantDiffuseColor = (Windows.UI.Color)x;

        private float _DistantDiffuseLightingDiffuseConstant = 1.0f;
        private float DistantDiffuseLightingDiffuseConstant
        {
            get => _DistantDiffuseLightingDiffuseConstant;
            set
            {
                _DistantDiffuseLightingDiffuseConstant = value;
                EffectDistantDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantDiffuseLightingDiffuseConstant)));
            }
        }
        public double GetDistantDiffuseLightingDiffuseConstant(float? x) => _DistantDiffuseLightingDiffuseConstant;
        public float? SetDistantDiffuseLightingDiffuseConstant(double x) => DistantDiffuseLightingDiffuseConstant = (float)x;

        private float _DistantDiffuseLightingSurfaceScale = 1.0f;
        private float DistantDiffuseLightingSurfaceScale
        {
            get => _DistantDiffuseLightingSurfaceScale;
            set
            {
                _DistantDiffuseLightingSurfaceScale = value;
                EffectDistantDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantDiffuseLightingSurfaceScale)));
            }
        }
        public double GetDistantDiffuseLightingSurfaceScale(float? x) => _DistantDiffuseLightingSurfaceScale;
        public float? SetDistantDiffuseLightingSurfaceScale(double x) => DistantDiffuseLightingSurfaceScale = (float)x;

        private uint _ScaleModeDistantDiffuse = (uint)D2D1_DISTANTDIFFUSE_SCALE_MODE.D2D1_DISTANTDIFFUSE_SCALE_MODE_LINEAR;

        // Distant-specular Lighting

        private float _DistantSpecularLightingAzimuth = 0.0f;
        private float DistantSpecularLightingAzimuth
        {
            get => _DistantSpecularLightingAzimuth;
            set
            {
                _DistantSpecularLightingAzimuth = value;
                EffectDistantSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantSpecularLightingAzimuth)));
            }
        }
        public double GetDistantSpecularLightingAzimuth(float? x) => _DistantSpecularLightingAzimuth;
        public float? SetDistantSpecularLightingAzimuth(double x) => DistantSpecularLightingAzimuth = (float)x;

        private float _DistantSpecularLightingElevation = 0.0f;
        private float DistantSpecularLightingElevation
        {
            get => _DistantSpecularLightingElevation;
            set
            {
                _DistantSpecularLightingElevation = value;
                EffectDistantSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantSpecularLightingElevation)));
            }
        }
        public double GetDistantSpecularLightingElevation(float? x) => _DistantSpecularLightingElevation;
        public float? SetDistantSpecularLightingElevation(double x) => DistantSpecularLightingElevation = (float)x;

        private Windows.UI.Color _DistantSpecularColor = Microsoft.UI.Colors.Blue;
        private Windows.UI.Color DistantSpecularColor
        {
            get => _DistantSpecularColor;
            set
            {
                _DistantSpecularColor = value;
                EffectDistantSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantSpecularColor)));
            }
        }
        public Windows.UI.Color GetDistantSpecularColor(Windows.UI.Color? x) => _DistantSpecularColor;
        public Windows.UI.Color? SetDistantSpecularColor(Windows.UI.Color x) => DistantSpecularColor = (Windows.UI.Color)x;

        private float _DistantSpecularLightingSpecularConstant = 1.0f;
        private float DistantSpecularLightingSpecularConstant
        {
            get => _DistantSpecularLightingSpecularConstant;
            set
            {
                _DistantSpecularLightingSpecularConstant = value;
                EffectDistantSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantSpecularLightingSpecularConstant)));
            }
        }
        public double GetDistantSpecularLightingSpecularConstant(float? x) => _DistantSpecularLightingSpecularConstant;
        public float? SetDistantSpecularLightingSpecularConstant(double x) => DistantSpecularLightingSpecularConstant = (float)x;

        private float _DistantSpecularLightingSurfaceScale = 1.0f;
        private float DistantSpecularLightingSurfaceScale
        {
            get => _DistantSpecularLightingSurfaceScale;
            set
            {
                _DistantSpecularLightingSurfaceScale = value;
                EffectDistantSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantSpecularLightingSurfaceScale)));
            }
        }
        public double GetDistantSpecularLightingSurfaceScale(float? x) => _DistantSpecularLightingSurfaceScale;
        public float? SetDistantSpecularLightingSurfaceScale(double x) => DistantSpecularLightingSurfaceScale = (float)x;

        private float _DistantSpecularLightingExponent = 1.0f;
        private float DistantSpecularLightingExponent
        {
            get => _DistantSpecularLightingExponent;
            set
            {
                _DistantSpecularLightingExponent = value;
                EffectDistantSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DistantSpecularLightingExponent)));
            }
        }
        public double GetDistantSpecularLightingExponent(float? x) => _DistantSpecularLightingExponent;
        public float? SetDistantSpecularLightingExponent(double x) => DistantSpecularLightingExponent = (float)x;

        private uint _ScaleModeDistantSpecular = (uint)D2D1_DISTANTSPECULAR_SCALE_MODE.D2D1_DISTANTSPECULAR_SCALE_MODE_LINEAR;

        // Emboss

        private float _EmbossHeight = 1.0f;
        private float EmbossHeight
        {
            get => _EmbossHeight;
            set
            {
                _EmbossHeight = value;
                EffectEmboss();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EmbossHeight)));
            }
        }
        public double GetEmbossHeight(float? x) => _EmbossHeight;
        public float? SetEmbossHeight(double x) => EmbossHeight = (float)x;

        private float _EmbossDirection = 0.0f;
        private float EmbossDirection
        {
            get => _EmbossDirection;
            set
            {
                _EmbossDirection = value;
                EffectEmboss();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EmbossDirection)));
            }
        }
        public double GetEmbossDirection(float? x) => _EmbossDirection;
        public float? SetEmbossDirection(double x) => EmbossDirection = (float)x;

        // Point-diffuse Lighting

        private Windows.UI.Color _PointDiffuseColor = Microsoft.UI.Colors.Violet;
        private Windows.UI.Color PointDiffuseColor
        {
            get => _PointDiffuseColor;
            set
            {
                _PointDiffuseColor = value;
                EffectPointDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointDiffuseColor)));
            }
        }
        public Windows.UI.Color GetPointDiffuseColor(Windows.UI.Color? x) => _PointDiffuseColor;
        public Windows.UI.Color? SetPointDiffuseColor(Windows.UI.Color x) => PointDiffuseColor = (Windows.UI.Color)x;

        private float _PointDiffuseLightingDiffuseConstant = 1.0f;
        private float PointDiffuseLightingDiffuseConstant
        {
            get => _PointDiffuseLightingDiffuseConstant;
            set
            {
                _PointDiffuseLightingDiffuseConstant = value;
                EffectPointDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointDiffuseLightingDiffuseConstant)));
            }
        }
        public double GetPointDiffuseLightingDiffuseConstant(float? x) => _PointDiffuseLightingDiffuseConstant;
        public float? SetPointDiffuseLightingDiffuseConstant(double x) => PointDiffuseLightingDiffuseConstant = (float)x;

        private float _PointDiffuseLightingSurfaceScale = 1.0f;
        private float PointDiffuseLightingSurfaceScale
        {
            get => _PointDiffuseLightingSurfaceScale;
            set
            {
                _PointDiffuseLightingSurfaceScale = value;
                EffectPointDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointDiffuseLightingSurfaceScale)));
            }
        }
        public double GetPointDiffuseLightingSurfaceScale(float? x) => _PointDiffuseLightingSurfaceScale;
        public float? SetPointDiffuseLightingSurfaceScale(double x) => PointDiffuseLightingSurfaceScale = (float)x;

        private uint _ScaleModePointDiffuse = (uint)D2D1_POINTDIFFUSE_SCALE_MODE.D2D1_POINTDIFFUSE_SCALE_MODE_LINEAR;

        private float _PointDiffuseLightingX = 0.0f;
        private float _PointDiffuseLightingY = 0.0f;

        private float _PointDiffuseLightingZ = 50.0f;
        private float PointDiffuseLightingZ
        {
            get => _PointDiffuseLightingZ;
            set
            {
                _PointDiffuseLightingZ = value;
                EffectPointDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointDiffuseLightingZ)));
            }
        }
        public double GetPointDiffuseLightingZ(float? x) => _PointDiffuseLightingZ;
        public float? SetPointDiffuseLightingZ(double x) => PointDiffuseLightingZ = (float)x;

        // Point-specular Lighting

        private Windows.UI.Color _PointSpecularColor = Microsoft.UI.Colors.Violet;
        private Windows.UI.Color PointSpecularColor
        {
            get => _PointSpecularColor;
            set
            {
                _PointSpecularColor = value;
                EffectPointSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointSpecularColor)));
            }
        }
        public Windows.UI.Color GetPointSpecularColor(Windows.UI.Color? x) => _PointSpecularColor;
        public Windows.UI.Color? SetPointSpecularColor(Windows.UI.Color x) => PointSpecularColor = (Windows.UI.Color)x;

        private float _PointSpecularLightingSpecularExponent = 1.0f;
        private float PointSpecularLightingSpecularExponent
        {
            get => _PointSpecularLightingSpecularExponent;
            set
            {
                _PointSpecularLightingSpecularExponent = value;
                EffectPointSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointSpecularLightingSpecularExponent)));
            }
        }
        public double GetPointSpecularLightingSpecularExponent(float? x) => _PointSpecularLightingSpecularExponent;
        public float? SetPointSpecularLightingSpecularExponent(double x) => PointSpecularLightingSpecularExponent = (float)x;

        private float _PointSpecularLightingSpecularConstant = 1.0f;
        private float PointSpecularLightingSpecularConstant
        {
            get => _PointSpecularLightingSpecularConstant;
            set
            {
                _PointSpecularLightingSpecularConstant = value;
                EffectPointSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointSpecularLightingSpecularConstant)));
            }
        }
        public double GetPointSpecularLightingSpecularConstant(float? x) => _PointSpecularLightingSpecularConstant;
        public float? SetPointSpecularLightingSpecularConstant(double x) => PointSpecularLightingSpecularConstant = (float)x;

        private float _PointSpecularLightingSurfaceScale = 1.0f;
        private float PointSpecularLightingSurfaceScale
        {
            get => _PointSpecularLightingSurfaceScale;
            set
            {
                _PointSpecularLightingSurfaceScale = value;
                EffectPointSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointSpecularLightingSurfaceScale)));
            }
        }
        public double GetPointSpecularLightingSurfaceScale(float? x) => _PointSpecularLightingSurfaceScale;
        public float? SetPointSpecularLightingSurfaceScale(double x) => PointSpecularLightingSurfaceScale = (float)x;

        private uint _ScaleModePointSpecular = (uint)D2D1_POINTSPECULAR_SCALE_MODE.D2D1_POINTSPECULAR_SCALE_MODE_LINEAR;

        private float _PointSpecularLightingX = 0.0f;
        private float _PointSpecularLightingY = 0.0f;

        private float _PointSpecularLightingZ = 50.0f;
        private float PointSpecularLightingZ
        {
            get => _PointSpecularLightingZ;
            set
            {
                _PointSpecularLightingZ = value;
                EffectPointSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PointSpecularLightingZ)));
            }
        }
        public double GetPointSpecularLightingZ(float? x) => _PointSpecularLightingZ;
        public float? SetPointSpecularLightingZ(double x) => PointSpecularLightingZ = (float)x;

        // Posterize

        private float _RedValueCount = 4.0f;
        private float RedValueCount
        {
            get => _RedValueCount;
            set
            {
                _RedValueCount = value;
                EffectPosterize();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedValueCount)));
            }
        }
        public double GetRedValueCount(float? x) => _RedValueCount;
        public float? SetRedValueCount(double x) => RedValueCount = (float)x;

        private float _GreenValueCount = 4.0f;
        private float GreenValueCount
        {
            get => _GreenValueCount;
            set
            {
                _GreenValueCount = value;
                EffectPosterize();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GreenValueCount)));
            }
        }
        public double GetGreenValueCount(float? x) => _GreenValueCount;
        public float? SetGreenValueCount(double x) => GreenValueCount = (float)x;

        private float _BlueValueCount = 4.0f;
        private float BlueValueCount
        {
            get => _BlueValueCount;
            set
            {
                _BlueValueCount = value;
                EffectPosterize();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlueValueCount)));
            }
        }
        public double GetBlueValueCount(float? x) => _BlueValueCount;
        public float? SetBlueValueCount(double x) => BlueValueCount = (float)x;

        // Spot-diffuse Lighting

        private Windows.UI.Color _SpotDiffuseColor = Microsoft.UI.Colors.Violet;
        private Windows.UI.Color SpotDiffuseColor
        {
            get => _SpotDiffuseColor;
            set
            {
                _SpotDiffuseColor = value;
                EffectSpotDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotDiffuseColor)));
            }
        }
        public Windows.UI.Color GetSpotDiffuseColor(Windows.UI.Color? x) => _SpotDiffuseColor;
        public Windows.UI.Color? SetSpotDiffuseColor(Windows.UI.Color x) => SpotDiffuseColor = (Windows.UI.Color)x;

        private float _SpotDiffuseLightingDiffuseConstant = 1.0f;
        private float SpotDiffuseLightingDiffuseConstant
        {
            get => _SpotDiffuseLightingDiffuseConstant;
            set
            {
                _SpotDiffuseLightingDiffuseConstant = value;
                EffectSpotDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotDiffuseLightingDiffuseConstant)));
            }
        }
        public double GetSpotDiffuseLightingDiffuseConstant(float? x) => _SpotDiffuseLightingDiffuseConstant;
        public float? SetSpotDiffuseLightingDiffuseConstant(double x) => SpotDiffuseLightingDiffuseConstant = (float)x;

        private float _SpotDiffuseLightingSurfaceScale = 1.0f;
        private float SpotDiffuseLightingSurfaceScale
        {
            get => _SpotDiffuseLightingSurfaceScale;
            set
            {
                _SpotDiffuseLightingSurfaceScale = value;
                EffectSpotDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotDiffuseLightingSurfaceScale)));
            }
        }
        public double GetSpotDiffuseLightingSurfaceScale(float? x) => _SpotDiffuseLightingSurfaceScale;
        public float? SetSpotDiffuseLightingSurfaceScale(double x) => SpotDiffuseLightingSurfaceScale = (float)x;

        private float _SpotDiffuseLightingFocus = 1.0f;
        private float SpotDiffuseLightingFocus
        {
            get => _SpotDiffuseLightingFocus;
            set
            {
                _SpotDiffuseLightingFocus = value;
                EffectSpotDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotDiffuseLightingFocus)));
            }
        }
        public double GetSpotDiffuseLightingFocus(float? x) => _SpotDiffuseLightingFocus;
        public float? SetSpotDiffuseLightingFocus(double x) => SpotDiffuseLightingFocus = (float)x;

        private float _SpotDiffuseLightingLimitingConeAngle = 90.0f;
        private float SpotDiffuseLightingLimitingConeAngle
        {
            get => _SpotDiffuseLightingLimitingConeAngle;
            set
            {
                _SpotDiffuseLightingLimitingConeAngle = value;
                EffectSpotDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotDiffuseLightingLimitingConeAngle)));
            }
        }
        public double GetSpotDiffuseLightingLimitingConeAngle(float? x) => _SpotDiffuseLightingLimitingConeAngle;
        public float? SetSpotDiffuseLightingLimitingConeAngle(double x) => SpotDiffuseLightingLimitingConeAngle = (float)x;

        private uint _ScaleModeSpotDiffuse = (uint)D2D1_SPOTDIFFUSE_SCALE_MODE.D2D1_SPOTDIFFUSE_SCALE_MODE_LINEAR;

        private float _SpotDiffuseLightingX = 0.0f;
        private float _SpotDiffuseLightingY = 0.0f;

        private float _SpotDiffuseLightingZ = 50.0f;
        private float SpotDiffuseLightingZ
        {
            get => _SpotDiffuseLightingZ;
            set
            {
                _SpotDiffuseLightingZ = value;
                EffectSpotDiffuseLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotDiffuseLightingZ)));
            }
        }
        public double GetSpotDiffuseLightingZ(float? x) => _SpotDiffuseLightingZ;
        public float? SetSpotDiffuseLightingZ(double x) => SpotDiffuseLightingZ = (float)x;

        // Spot-specular Lighting

        private Windows.UI.Color _SpotSpecularColor = Microsoft.UI.Colors.Violet;
        private Windows.UI.Color SpotSpecularColor
        {
            get => _SpotSpecularColor;
            set
            {
                _SpotSpecularColor = value;
                EffectSpotSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotSpecularColor)));
            }
        }
        public Windows.UI.Color GetSpotSpecularColor(Windows.UI.Color? x) => _SpotSpecularColor;
        public Windows.UI.Color? SetSpotSpecularColor(Windows.UI.Color x) => SpotSpecularColor = (Windows.UI.Color)x;

        private float _SpotSpecularLightingSpecularConstant = 1.0f;
        private float SpotSpecularLightingSpecularConstant
        {
            get => _SpotSpecularLightingSpecularConstant;
            set
            {
                _SpotSpecularLightingSpecularConstant = value;
                EffectSpotSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotSpecularLightingSpecularConstant)));
            }
        }
        public double GetSpotSpecularLightingSpecularConstant(float? x) => _SpotSpecularLightingSpecularConstant;
        public float? SetSpotSpecularLightingSpecularConstant(double x) => SpotSpecularLightingSpecularConstant = (float)x;

        private float _SpotSpecularLightingSurfaceScale = 1.0f;
        private float SpotSpecularLightingSurfaceScale
        {
            get => _SpotSpecularLightingSurfaceScale;
            set
            {
                _SpotSpecularLightingSurfaceScale = value;
                EffectSpotSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotSpecularLightingSurfaceScale)));
            }
        }
        public double GetSpotSpecularLightingSurfaceScale(float? x) => _SpotSpecularLightingSurfaceScale;
        public float? SetSpotSpecularLightingSurfaceScale(double x) => SpotSpecularLightingSurfaceScale = (float)x;

        private float _SpotSpecularLightingSpecularExponent = 1.0f;
        private float SpotSpecularLightingSpecularExponent
        {
            get => _SpotSpecularLightingSpecularExponent;
            set
            {
                _SpotSpecularLightingSpecularExponent = value;
                EffectSpotSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotSpecularLightingSpecularExponent)));
            }
        }
        public double GetSpotSpecularLightingSpecularExponent(float? x) => _SpotSpecularLightingSpecularExponent;
        public float? SetSpotSpecularLightingSpecularExponent(double x) => SpotSpecularLightingSpecularExponent = (float)x;

        private float _SpotSpecularLightingFocus = 1.0f;
        private float SpotSpecularLightingFocus
        {
            get => _SpotSpecularLightingFocus;
            set
            {
                _SpotSpecularLightingFocus = value;
                EffectSpotSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotSpecularLightingFocus)));
            }
        }
        public double GetSpotSpecularLightingFocus(float? x) => _SpotSpecularLightingFocus;
        public float? SetSpotSpecularLightingFocus(double x) => SpotSpecularLightingFocus = (float)x;

        private float _SpotSpecularLightingLimitingConeAngle = 90.0f;
        private float SpotSpecularLightingLimitingConeAngle
        {
            get => _SpotSpecularLightingLimitingConeAngle;
            set
            {
                _SpotSpecularLightingLimitingConeAngle = value;
                EffectSpotSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotSpecularLightingLimitingConeAngle)));
            }
        }
        public double GetSpotSpecularLightingLimitingConeAngle(float? x) => _SpotSpecularLightingLimitingConeAngle;
        public float? SetSpotSpecularLightingLimitingConeAngle(double x) => SpotSpecularLightingLimitingConeAngle = (float)x;

        private uint _ScaleModeSpotSpecular = (uint)D2D1_SPOTSPECULAR_SCALE_MODE.D2D1_SPOTSPECULAR_SCALE_MODE_LINEAR;

        private float _SpotSpecularLightingX = 0.0f;
        private float _SpotSpecularLightingY = 0.0f;

        private float _SpotSpecularLightingZ = 50.0f;
        private float SpotSpecularLightingZ
        {
            get => _SpotSpecularLightingZ;
            set
            {
                _SpotSpecularLightingZ = value;
                EffectSpotSpecularLighting();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SpotSpecularLightingZ)));
            }
        }
        public double GetSpotSpecularLightingZ(float? x) => _SpotSpecularLightingZ;
        public float? SetSpotSpecularLightingZ(double x) => SpotSpecularLightingZ = (float)x;

        // Brightness

        private float _BrightnessWhitePointX = 1.0f;
        private float BrightnessWhitePointX
        {
            get => _BrightnessWhitePointX;
            set
            {
                _BrightnessWhitePointX = value;
                EffectBrightness();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BrightnessWhitePointX)));
            }
        }
        public double GetBrightnessWhitePointX(float? x) => _BrightnessWhitePointX;
        public float? SetBrightnessWhitePointX(double x) => BrightnessWhitePointX = (float)x;

        private float _BrightnessWhitePointY = 1.0f;
        private float BrightnessWhitePointY
        {
            get => _BrightnessWhitePointY;
            set
            {
                _BrightnessWhitePointY = value;
                EffectBrightness();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BrightnessWhitePointY)));
            }
        }
        public double GetBrightnessWhitePointY(float? x) => _BrightnessWhitePointY;
        public float? SetBrightnessWhitePointY(double x) => BrightnessWhitePointY = (float)x;

        private float _BrightnessBlackPointX = 0.0f;
        private float BrightnessBlackPointX
        {
            get => _BrightnessBlackPointX;
            set
            {
                _BrightnessBlackPointX = value;
                EffectBrightness();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BrightnessBlackPointX)));
            }
        }
        public double GetBrightnessBlackPointX(float? x) => _BrightnessBlackPointX;
        public float? SetBrightnessBlackPointX(double x) => BrightnessBlackPointX = (float)x;

        private float _BrightnessBlackPointY = 0.0f;
        private float BrightnessBlackPointY
        {
            get => _BrightnessBlackPointY;
            set
            {
                _BrightnessBlackPointY = value;
                EffectBrightness();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BrightnessBlackPointY)));
            }
        }
        public double GetBrightnessBlackPointY(float? x) => _BrightnessBlackPointY;
        public float? SetBrightnessBlackPointY(double x) => BrightnessBlackPointY = (float)x;

        // Saturation

        private float _Contrast = 0.0f;
        private float Contrast
        {
            get => _Contrast;
            set
            {
                _Contrast = value;
                EffectContrast();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Contrast)));
            }
        }
        public double GetContrast(float? x) => _Contrast;
        public float? SetContrast(double x) => Contrast = (float)x;

        // Exposure

        private float _Exposure = 0.0f;
        private float Exposure
        {
            get => _Exposure;
            set
            {
                _Exposure = value;
                EffectExposure();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exposure)));
            }
        }
        public double GetExposure(float? x) => _Exposure;
        public float? SetExposure(double x) => Exposure = (float)x;

        // Highlights And Shadows

        private float _HighlightsAndShadowsHighlights = 0.0f;
        private float HighlightsAndShadowsHighlights
        {
            get => _HighlightsAndShadowsHighlights;
            set
            {
                _HighlightsAndShadowsHighlights = value;
                EffectHighlightsAndShadows();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HighlightsAndShadowsHighlights)));
            }
        }
        public double GetHighlightsAndShadowsHighlights(float? x) => _HighlightsAndShadowsHighlights;
        public float? SetHighlightsAndShadowsHighlights(double x) => HighlightsAndShadowsHighlights = (float)x;

        private float _HighlightsAndShadowsShadows = 0.0f;
        private float HighlightsAndShadowsShadows
        {
            get => _HighlightsAndShadowsShadows;
            set
            {
                _HighlightsAndShadowsShadows = value;
                EffectHighlightsAndShadows();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HighlightsAndShadowsShadows)));
            }
        }
        public double GetHighlightsAndShadowsShadows(float? x) => _HighlightsAndShadowsShadows;
        public float? SetHighlightsAndShadowsShadows(double x) => HighlightsAndShadowsShadows = (float)x;

        private float _HighlightsAndShadowsClarity = 0.0f;
        private float HighlightsAndShadowsClarity
        {
            get => _HighlightsAndShadowsClarity;
            set
            {
                _HighlightsAndShadowsClarity = value;
                EffectHighlightsAndShadows();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HighlightsAndShadowsClarity)));
            }
        }
        public double GetHighlightsAndShadowsClarity(float? x) => _HighlightsAndShadowsClarity;
        public float? SetHighlightsAndShadowsClarity(double x) => HighlightsAndShadowsClarity = (float)x;

        private float _HighlightsAndShadowsMaskBlurRadius = 1.25f;
        private float HighlightsAndShadowsMaskBlurRadius
        {
            get => _HighlightsAndShadowsMaskBlurRadius;
            set
            {
                _HighlightsAndShadowsMaskBlurRadius = value;
                EffectHighlightsAndShadows();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HighlightsAndShadowsMaskBlurRadius)));
            }
        }
        public double GetHighlightsAndShadowsMaskBlurRadius(float? x) => _HighlightsAndShadowsMaskBlurRadius;
        public float? SetHighlightsAndShadowsMaskBlurRadius(double x) => HighlightsAndShadowsMaskBlurRadius = (float)x;

        public uint _HighlightsAndShadowsMaskInputGamma = (uint)D2D1_HIGHLIGHTSANDSHADOWS_INPUT_GAMMA.D2D1_HIGHLIGHTSANDSHADOWS_INPUT_GAMMA_LINEAR;

        // Sepia

        private float _SepiaIntensity = 0.5f;
        private float SepiaIntensity
        {
            get => _SepiaIntensity;
            set
            {
                _SepiaIntensity = value;
                EffectSepia();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SepiaIntensity)));
            }
        }
        public double GetSepiaIntensity(float? x) => _SepiaIntensity;
        public float? SetSepiaIntensity(double x) => SepiaIntensity = (float)x;

        // Sharpen        

        private float _SharpenSharpness = 0.0f;
        private float SharpenSharpness
        {
            get => _SharpenSharpness;
            set
            {
                _SharpenSharpness = value;
                EffectSharpen();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SharpenSharpness)));
            }
        }
        public double GetSharpenSharpness(float? x) => _SharpenSharpness;
        public float? SetSharpenSharpness(double x) => SharpenSharpness = (float)x;

        private float _SharpenThreshold = 0.0f;
        private float SharpenThreshold
        {
            get => _SharpenThreshold;
            set
            {
                _SharpenThreshold = value;
                EffectSharpen();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SharpenThreshold)));
            }
        }
        public double GetSharpenThreshold(float? x) => _SharpenThreshold;
        public float? SetSharpenThreshold(double x) => SharpenThreshold = (float)x;

        // Straighten

        private float _StraightenAngle = 0.0f;
        private float StraightenAngle
        {
            get => _StraightenAngle;
            set
            {
                _StraightenAngle = value;
                EffectStraighten();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StraightenAngle)));
            }
        }
        public double GetStraightenAngle(float? x) => _StraightenAngle;
        public float? SetStraightenAngle(double x) => StraightenAngle = (float)x;

        private uint _ScaleModeStraighten = (uint)D2D1_STRAIGHTEN_SCALE_MODE.D2D1_STRAIGHTEN_SCALE_MODE_LINEAR;

        // Temperature And Tint        

        private float _TemperatureAndTintTemperature = 0.0f;
        private float TemperatureAndTintTemperature
        {
            get => _TemperatureAndTintTemperature;
            set
            {
                _TemperatureAndTintTemperature = value;
                EffectTemperatureAndTint();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TemperatureAndTintTemperature)));
            }
        }
        public double GetTemperatureAndTintTemperature(float? x) => _TemperatureAndTintTemperature;
        public float? SetTemperatureAndTintTemperature(double x) => TemperatureAndTintTemperature = (float)x;

        private float _TemperatureAndTintTint = 0.0f;
        private float TemperatureAndTintTint
        {
            get => _TemperatureAndTintTint;
            set
            {
                _TemperatureAndTintTint = value;
                EffectTemperatureAndTint();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TemperatureAndTintTint)));
            }
        }
        public double GetTemperatureAndTintTint(float? x) => _TemperatureAndTintTint;
        public float? SetTemperatureAndTintTint(double x) => TemperatureAndTintTint = (float)x;

        // Vignette

        private float _VignetteTransitionSize = 0.1f;
        private float VignetteTransitionSize
        {
            get => _VignetteTransitionSize;
            set
            {
                _VignetteTransitionSize = value;
                EffectVignette();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VignetteTransitionSize)));
            }
        }
        public double GetVignetteTransitionSize(float? x) => _VignetteTransitionSize;
        public float? SetVignetteTransitionSize(double x) => VignetteTransitionSize = (float)x;

        private float _VignetteStrength = 0.5f;
        private float VignetteStrength
        {
            get => _VignetteStrength;
            set
            {
                _VignetteStrength = value;
                EffectVignette();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VignetteStrength)));
            }
        }
        public double GetVignetteStrength(float? x) => _VignetteStrength;
        public float? SetVignetteStrength(double x) => VignetteStrength = (float)x;

        private Windows.UI.Color _VignetteColor = Microsoft.UI.Colors.Blue;
        private Windows.UI.Color VignetteColor
        {
            get => _VignetteColor;
            set
            {
                _VignetteColor = value;
                EffectVignette();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VignetteColor)));
            }
        }
        public Windows.UI.Color GetVignetteColor(Windows.UI.Color? x) => _VignetteColor;
        public Windows.UI.Color? SetVignetteColor(Windows.UI.Color x) => VignetteColor = (Windows.UI.Color)x;

        // 2D Affine Transform

        private uint _BorderModeAffineTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
        private uint _InterpolationModeAffineTransform = (uint)D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE.D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_LINEAR;

        private float _AffineTransformSharpness = 0.0f;
        private float AffineTransformSharpness
        {
            get => _AffineTransformSharpness;
            set
            {
                _AffineTransformSharpness = value;
                EffectAffineTransform();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AffineTransformSharpness)));
            }
        }
        public double GetAffineTransformSharpness(float? x) => _AffineTransformSharpness;
        public float? SetAffineTransformSharpness(double x) => AffineTransformSharpness = (float)x;

        // 3D Transform

        private uint _BorderModeTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
        private uint _InterpolationModeTransform = (uint)D2D1_3DTRANSFORM_INTERPOLATION_MODE.D2D1_3DTRANSFORM_INTERPOLATION_MODE_LINEAR;

        // Perspective Transform

        private uint _BorderModePerspectiveTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
        private uint _InterpolationModePerspectiveTransform = (uint)D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE.D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE_LINEAR;

        private float _PerspectiveTransformRotationAngleX = 0.0f;
        private float PerspectiveTransformRotationAngleX
        {
            get => _PerspectiveTransformRotationAngleX;
            set
            {
                _PerspectiveTransformRotationAngleX = value;
                EffectPerspectiveTransform();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PerspectiveTransformRotationAngleX)));
            }
        }
        public double GetPerspectiveTransformRotationAngleX(float? x) => _PerspectiveTransformRotationAngleX;
        public float? SetPerspectiveTransformRotationAngleX(double x) => PerspectiveTransformRotationAngleX = (float)x;

        private float _PerspectiveTransformRotationAngleY = 0.0f;
        private float PerspectiveTransformRotationAngleY
        {
            get => _PerspectiveTransformRotationAngleY;
            set
            {
                _PerspectiveTransformRotationAngleY = value;
                EffectPerspectiveTransform();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PerspectiveTransformRotationAngleY)));
            }
        }
        public double GetPerspectiveTransformRotationAngleY(float? x) => _PerspectiveTransformRotationAngleY;
        public float? SetPerspectiveTransformRotationAngleY(double x) => PerspectiveTransformRotationAngleY = (float)x;

        private float _PerspectiveTransformRotationAngleZ = 0.0f;
        private float PerspectiveTransformRotationAngleZ
        {
            get => _PerspectiveTransformRotationAngleZ;
            set
            {
                _PerspectiveTransformRotationAngleZ = value;
                EffectPerspectiveTransform();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PerspectiveTransformRotationAngleZ)));
            }
        }
        public double GetPerspectiveTransformRotationAngleZ(float? x) => _PerspectiveTransformRotationAngleZ;
        public float? SetPerspectiveTransformRotationAngleZ(double x) => PerspectiveTransformRotationAngleZ = (float)x;

        // Border

        private uint _BorderEdgeModeX = (uint)D2D1_BORDER_EDGE_MODE.D2D1_BORDER_EDGE_MODE_CLAMP;
        private uint _BorderEdgeModeY = (uint)D2D1_BORDER_EDGE_MODE.D2D1_BORDER_EDGE_MODE_CLAMP;

        // Crop

        private uint _BorderModeCrop = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;

        // Scale

        private uint _BorderModeScale = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
        private uint _InterpolationModeScale = (uint)D2D1_SCALE_INTERPOLATION_MODE.D2D1_SCALE_INTERPOLATION_MODE_LINEAR;

        private float _ScaleSharpness = 0.0f;
        private float ScaleSharpness
        {
            get => _ScaleSharpness;
            set
            {
                _ScaleSharpness = value;
                EffectScale();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScaleSharpness)));
            }
        }
        public double GetScaleSharpness(float? x) => _ScaleSharpness;
        public float? SetScaleSharpness(double x) => ScaleSharpness = (float)x;

        private float _ScaleScaleX = 1.0f;
        private float ScaleScaleX
        {
            get => _ScaleScaleX;
            set
            {
                _ScaleScaleX = value;
                EffectScale();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScaleScaleX)));
            }
        }
        public double GetScaleScaleX(float? x) => _ScaleScaleX;
        public float? SetScaleScaleX(double x) => ScaleScaleX = (float)x;

        private float _ScaleScaleY = 1.0f;
        private float ScaleScaleY
        {
            get => _ScaleScaleY;
            set
            {
                _ScaleScaleY = value;
                EffectScale();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScaleScaleY)));
            }
        }
        public double GetScaleScaleY(float? x) => _ScaleScaleY;
        public float? SetScaleScaleY(double x) => ScaleScaleY = (float)x;

        // Chroma-Key

        private Windows.UI.Color _ChromaKeyColor = Microsoft.UI.Colors.Green;
        private Windows.UI.Color ChromaKeyColor
        {
            get => _VignetteColor;
            set
            {
                _ChromaKeyColor = value;
                EffectChromaKey();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChromaKeyColor)));
            }
        }
        public Windows.UI.Color GetChromaKeyColor(Windows.UI.Color? x) => _ChromaKeyColor;
        public Windows.UI.Color? SetChromaKeyColor(Windows.UI.Color x) => ChromaKeyColor = (Windows.UI.Color)x;

        private float _ChromaKeyTolerance = 0.1f;
        private float ChromaKeyTolerance
        {
            get => _ChromaKeyTolerance;
            set
            {
                _ChromaKeyTolerance = value;
                EffectChromaKey();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChromaKeyTolerance)));
            }
        }
        public double GetChromaKeyTolerance(float? x) => _ChromaKeyTolerance;
        public float? SetChromaKeyTolerance(double x) => ChromaKeyTolerance = (float)x;

        // Luminance To Alpha

        private Windows.UI.Color _LuminanceToAlphaColor = Microsoft.UI.Colors.White;
        private Windows.UI.Color LuminanceToAlphaColor
        {
            get => _LuminanceToAlphaColor;
            set
            {
                _LuminanceToAlphaColor = value;
                EffectLuminanceToAlpha();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LuminanceToAlphaColor)));
            }
        }
        public Windows.UI.Color GetLuminanceToAlphaColor(Windows.UI.Color? x) => _LuminanceToAlphaColor;
        public Windows.UI.Color? SetLuminanceToAlphaColor(Windows.UI.Color x) => LuminanceToAlphaColor = (Windows.UI.Color)x;

        // Opacity

        private float _OpacityOpacity = 1.0f;
        private float OpacityOpacity
        {
            get => _OpacityOpacity;
            set
            {
                _OpacityOpacity = value;
                EffectOpacity();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OpacityOpacity)));
            }
        }
        public double GetOpacityOpacity(float? x) => _OpacityOpacity;
        public float? SetOpacityOpacity(double x) => OpacityOpacity = (float)x;

        // Blend
        private uint _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_MULTIPLY;

        // Composite
        private uint _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER;

        // Cross-Fade

        private float _CrossFadeWeight = 0.5f;
        private float CrossFadeWeight
        {
            get => _CrossFadeWeight;
            set
            {
                _CrossFadeWeight = value;
                EffectCrossFade();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CrossFadeWeight)));
            }
        }
        public double GetCrossFadeWeight(float? x) => _CrossFadeWeight;
        public float? SetCrossFadeWeight(double x) => CrossFadeWeight = (float)x;

        // Turbulence

        private float _TurbulenceBaseFrequencyX = 0.01f;
        private float TurbulenceBaseFrequencyX
        {
            get => _TurbulenceBaseFrequencyX;
            set
            {
                _TurbulenceBaseFrequencyX = value;
                EffectTurbulence();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurbulenceBaseFrequencyX)));
            }
        }
        public double GetTurbulenceBaseFrequencyX(float? x) => _TurbulenceBaseFrequencyX;
        public float? SetTurbulenceBaseFrequencyX(double x) => TurbulenceBaseFrequencyX = (float)x;

        private float _TurbulenceBaseFrequencyY = 0.01f;
        private float TurbulenceBaseFrequencyY
        {
            get => _TurbulenceBaseFrequencyY;
            set
            {
                _TurbulenceBaseFrequencyY = value;
                EffectTurbulence();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurbulenceBaseFrequencyY)));
            }
        }
        public double GetTurbulenceBaseFrequencyY(float? x) => _TurbulenceBaseFrequencyY;
        public float? SetTurbulenceBaseFrequencyY(double x) => TurbulenceBaseFrequencyY = (float)x;

        private float _TurbulenceNumOctaves = 1;
        private float TurbulenceNumOctaves
        {
            get => _TurbulenceNumOctaves;
            set
            {
                _TurbulenceNumOctaves = value;
                EffectTurbulence();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurbulenceNumOctaves)));
            }
        }
        public double GetTurbulenceNumOctaves(float? x) => _TurbulenceNumOctaves;
        public float? SetTurbulenceNumOctaves(double x) => TurbulenceNumOctaves = (float)x;

        private float _TurbulenceSeed = 0;
        private float TurbulenceSeed
        {
            get => _TurbulenceSeed;
            set
            {
                _TurbulenceSeed = value;
                EffectTurbulence();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurbulenceSeed)));
            }
        }
        public double GetTurbulenceSeed(float? x) => _TurbulenceSeed;
        public float? SetTurbulenceSeed(double x) => TurbulenceSeed = (float)x;

        private uint _TurbulenceNoise = (uint)D2D1_TURBULENCE_NOISE.D2D1_TURBULENCE_NOISE_FRACTAL_SUM;

        // Shadow

        private float _ShadowBlurStandardDeviation = 3.0f;
        private float ShadowBlurStandardDeviation
        {
            get => _ShadowBlurStandardDeviation;
            set
            {
                _ShadowBlurStandardDeviation = value;
                EffectShadow();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShadowBlurStandardDeviation)));
            }
        }
        public double GetShadowBlurStandardDeviation(float? x) => _ShadowBlurStandardDeviation;
        public float? SetShadowBlurStandardDeviation(double x) => ShadowBlurStandardDeviation = (float)x;

        private float _ShadowTranslate = 20.0f;
        private float ShadowTranslate
        {
            get => _ShadowTranslate;
            set
            {
                _ShadowTranslate = value;
                EffectShadow();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShadowTranslate)));
            }
        }
        public double GetShadowTranslate(float? x) => _ShadowTranslate;
        public float? SetShadowTranslate(double x) => ShadowTranslate = (float)x;

        private Windows.UI.Color _ShadowColor = Microsoft.UI.Colors.DarkSlateGray;
        private Windows.UI.Color ShadowColor
        {
            get => _ShadowColor;
            set
            {
                _ShadowColor = value;
                EffectShadow();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShadowColor)));
            }
        }
        public Windows.UI.Color GetShadowColor(Windows.UI.Color? x) => _ShadowColor;
        public Windows.UI.Color? SetShadowColor(Windows.UI.Color x) => ShadowColor = (Windows.UI.Color)x;

        private uint _OptimizationShadow = (uint)D2D1_SHADOW_OPTIMIZATION.D2D1_SHADOW_OPTIMIZATION_BALANCED;

        // Displacement Map

        private float _DisplacementMapScale = 0.0f;
        private float DisplacementMapScale
        {
            get => _DisplacementMapScale;
            set
            {
                _DisplacementMapScale = value;
                EffectDisplacementMap();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplacementMapScale)));
            }
        }
        public double GetDisplacementMapScale(float? x) => _DisplacementMapScale;
        public float? SetDisplacementMapScale(double x) => DisplacementMapScale = (float)x;

        private float _TurbulenceBaseFrequencyDMX = 0.01f;
        private float TurbulenceBaseFrequencyDMX
        {
            get => _TurbulenceBaseFrequencyDMX;
            set
            {
                _TurbulenceBaseFrequencyDMX = value;
                EffectDisplacementMap();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurbulenceBaseFrequencyDMX)));
            }
        }
        public double GetTurbulenceBaseFrequencyDMX(float? x) => _TurbulenceBaseFrequencyDMX;
        public float? SetTurbulenceBaseFrequencyDMX(double x) => TurbulenceBaseFrequencyDMX = (float)x;

        private float _TurbulenceBaseFrequencyDMY = 0.01f;
        private float TurbulenceBaseFrequencyDMY
        {
            get => _TurbulenceBaseFrequencyDMY;
            set
            {
                _TurbulenceBaseFrequencyDMY = value;
                EffectDisplacementMap();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurbulenceBaseFrequencyDMY)));
            }
        }
        public double GetTurbulenceBaseFrequencyDMY(float? x) => _TurbulenceBaseFrequencyDMY;
        public float? SetTurbulenceBaseFrequencyDMY(double x) => TurbulenceBaseFrequencyDMY = (float)x;

        private uint _ChannelX = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_A;
        private uint _ChannelY = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_A;


        //

        private bool m_bAnimate = false;
        private ulong nLastTime = 0, nTotalTime = 0;
        private uint nNbTotalFrames = 0, nLastNbFrames = 0;
        private void CompositionTarget_Rendering(object sender, object e)
        {
            HRESULT hr = HRESULT.S_OK;
            if (borderSCP.Visibility == Visibility.Visible)
            {
                Render();
                if (m_pDXGISwapChain1 != null)
                {
                    DXGI_FRAME_STATISTICS fs = new DXGI_FRAME_STATISTICS();
                    hr = m_pDXGISwapChain1.GetFrameStatistics(out fs);      
                    if (hr == HRESULT.S_OK)
                    {
                        ulong nCurrentTime = (ulong)fs.SyncQPCTime.QuadPart;
                        nNbTotalFrames += fs.PresentCount - nLastNbFrames;
                        if (nLastTime != 0)
                        {
                            double nSeconds = 0;
                            if (!m_bAnimate)
                            {
                                nTotalTime += (nCurrentTime - nLastTime);
                                nSeconds = nTotalTime / (ulong)_liFreq.QuadPart;
                            }
                            //if (nSeconds >= 1)
                            //{
                            //    //tbFPS.Text = nNbTotalFrames.ToString() + " FPS";
                            //    nNbTotalFrames = 0;
                            //    nTotalTime = 0;
                            //}
                            if (nSeconds >= 5)
                            {
                                //Console.Beep(8000, 10);
                                m_bAnimate = true;
                                nTotalTime = 0;
                            }
                        }
                        nLastNbFrames = fs.PresentCount;
                        nLastTime = nCurrentTime;
                    }
                }
            }
        }

        int nImage = 0;
        float nXTranslate = 1;
        float nCrossFadeWeight = 1.0f;
        float nRotationX = 0.0f;
        bool bRotation2 = false;       
        float nStandardDeviationGaussianBlur = 0.0f;
        float nBlurStep = 5.0f;
        float nCropX = 0.0f;
        float nCropY = 0.0f;
        float nBrightnessWhiteY = 1.0f;
        float nBrightnessWhiteYStep = -0.01f;
        float nRotationAngle = 0.0f;
        float nRotationScaleX = 0.0f;
        float nRotationScaleY = 0.0f;
        float nRadiusXStep = 1.0f;
        float nRadiusYStep = 1.0f;
        float nChromaKeyTolerance = 0.0f;
        float nChromaKeyToleranceStep = 0.01f;
        float nMorphologyWidth = 1.0f;
        float nMorphologyHeight = 1.0f;
        float nMorphologyStep = 1.0f;
        float nScaleX = 1.0f;
        float nScaleY = 1.0f;
        float nScaleStep = 0.1f;
        float nDisplacementMapScale = 1.0f;
        float nDisplacementMapScaleStep = 1.05f;

        HRESULT Render()
        {
            HRESULT hr = HRESULT.S_OK;
            if (m_pD2DDeviceContext != null)
            {
                m_pD2DDeviceContext.BeginDraw();
                m_pD2DDeviceContext.GetSize(out D2D1_SIZE_F size);              

                if (!m_bAnimate)
                {
                    // Should already be re-initialized                   
                    nXTranslate = 1;
                    nCrossFadeWeight = 1.0f;
                    nRotationX = 0.0f;
                    bRotation2 = false;
                    nStandardDeviationGaussianBlur = 0.0f;
                    nBlurStep = 5.0f;
                    nCropX = 0.0f;
                    nCropY = 0.0f; 
                    nBrightnessWhiteY = 1.0f;
                    nBrightnessWhiteYStep = -0.01f;
                    nRotationAngle = 0.0f;
                    nRotationScaleX = 0.0f;
                    nRotationScaleY = 0.0f;
                    nRadiusXStep = 1.0f;
                    nRadiusYStep = 1.0f;
                    nChromaKeyTolerance = 0.0f;
                    nChromaKeyToleranceStep = 0.01f;
                    nMorphologyWidth = 1.0f;
                    nMorphologyHeight = 1.0f;
                    nMorphologyStep = 1.0f;
                    nScaleX = 1.0f;
                    nScaleY = 1.0f;
                    nScaleStep = 0.1f;
                    nDisplacementMapScale = 1.0f;
                    nDisplacementMapScaleStep = 1.05f;

                    listImages[nImage].GetSize(out D2D1_SIZE_F sizeBmpBackground);
                    D2D1_RECT_F destRectBackground = new D2D1_RECT_F(0.0f, 0.0f, size.width, size.height);
                    D2D1_RECT_F sourceRectBackground = new D2D1_RECT_F(0.0f, 0.0f, sizeBmpBackground.width, sizeBmpBackground.height);
                    m_pD2DDeviceContext.DrawBitmap(listImages[nImage], ref destRectBackground, 1.0f, D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, ref sourceRectBackground);
                }
                else
                {
                    m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));
                    if (listImages[nImage] != null)
                    {
                        if (_Animation == (uint)ANIMATION.ANIMATION_TRANSLATE)
                        {
                            ID2D1Effect pCompositeEffect;
                            m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Composite, out pCompositeEffect);

                            ID2D1Effect pAffineTransformEffect2 = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect2);
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;
                            pAffineTransformEffect2.SetInput(0, listImages[nImage2]);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            imageRectangle.right = imageRectangle.left + bmpSize.width * (size.width / bmpSize.width);
                            imageRectangle.bottom = imageRectangle.top + bmpSize.height * (size.height / bmpSize.height);

                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            //D2D1_SIZE_F sizeTranslate = new D2D1_SIZE_F(100, 100);
                            //var translateMatrix = Matrix3x2F.Translation(sizeTranslate);
                            //float[] aFloatArray2 = {1.0f, 0.0f,
                            //    0.0f, 1.0f,
                            //    100, 100
                            //};

                            float[] aFloatArray = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                -nXTranslate, scaleMatrix._32
                            };
                            //_11 1.100141    float
                            //_22 0.7708565   float

                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray);

                            listImages[nImage2].GetSize(out D2D1_SIZE_F bmpSize2);
                            var scaleMatrix2 = Matrix3x2F.Scale(size.width / bmpSize2.width, size.height / bmpSize2.height);

                            float[] aFloatArray2 = {scaleMatrix2._11, scaleMatrix2._12,
                                scaleMatrix2._21, scaleMatrix2._22,
                                scaleMatrix2._31, scaleMatrix2._32
                            };

                            SetEffectFloatArray(pAffineTransformEffect2, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray2);

                            D2DTools.SetInputEffect(pCompositeEffect, 0, pAffineTransformEffect2);
                            D2DTools.SetInputEffect(pCompositeEffect, 1, pAffineTransformEffect);

                            ID2D1Image pOutputImage = null;
                            pCompositeEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nXTranslate *= 1.025f;
                            if (nXTranslate >= size.width)
                            {
                                nXTranslate = 1;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pCompositeEffect);
                            SafeRelease(ref pAffineTransformEffect);
                            SafeRelease(ref pAffineTransformEffect2);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_CROSSFADE)
                        {
                            ID2D1Effect pCrossFadeEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1CrossFade, out pCrossFadeEffect);
                            //pCrossFadeEffect.SetInput(0, listImages[nImage]);
                            //pCrossFadeEffect.SetInput(1, listImages[nImage2]);

                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray);

                            ID2D1Effect pAffineTransformEffect2 = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect2);
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;
                            pAffineTransformEffect2.SetInput(0, listImages[nImage2]);

                            listImages[nImage2].GetSize(out D2D1_SIZE_F bmpSize2);
                            var scaleMatrix2 = Matrix3x2F.Scale(size.width / bmpSize2.width, size.height / bmpSize2.height);

                            float[] aFloatArray2 = {scaleMatrix2._11, scaleMatrix2._12,
                                scaleMatrix2._21, scaleMatrix2._22,
                                scaleMatrix2._31, scaleMatrix2._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect2, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray2);

                            SetEffectFloat(pCrossFadeEffect, (uint)D2D1_CROSSFADE_PROP.D2D1_CROSSFADE_PROP_WEIGHT, nCrossFadeWeight);

                            D2DTools.SetInputEffect(pCrossFadeEffect, 0, pAffineTransformEffect);
                            D2DTools.SetInputEffect(pCrossFadeEffect, 1, pAffineTransformEffect2);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pCrossFadeEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nCrossFadeWeight /= 1.015f;
                            if (nCrossFadeWeight <= 0.015)
                            {
                                nCrossFadeWeight = 1.0f;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pCrossFadeEffect);
                            SafeRelease(ref pAffineTransformEffect);
                            SafeRelease(ref pAffineTransformEffect2);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_PERSPECTIVE)
                        {
                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            ID2D1Effect pPerspectiveTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D13DPerspectiveTransform, out pPerspectiveTransformEffect);
                            pPerspectiveTransformEffect.SetInput(0, listImages[nImage]);

                            SetEffectFloat(pPerspectiveTransformEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_DEPTH, bmpSize.width * 4);
                            float[] aFloatArray = { size.width / 2, 0, 0 };
                            SetEffectFloatArray(pPerspectiveTransformEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_ROTATION_ORIGIN, aFloatArray);

                            float[] aFloatArray2 = { 0, nRotationX, 0 };
                            SetEffectFloatArray(pPerspectiveTransformEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_ROTATION, aFloatArray2);

                            D2DTools.SetInputEffect(pPerspectiveTransformEffect, 0, pAffineTransformEffect);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pPerspectiveTransformEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nRotationX += 1.0f;
                            if (nRotationX >= 83.0f && !bRotation2)
                            {
                                nRotationX = -97.0f;
                                bRotation2 = true;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                            }
                            else if (nRotationX >= 0 && bRotation2)
                            {
                                m_bAnimate = false;
                                bRotation2 = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pAffineTransformEffect);
                            SafeRelease(ref pPerspectiveTransformEffect);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_BLUR)
                        {
                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            ID2D1Effect pBlurEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1GaussianBlur, out pBlurEffect);
                            //pBlurEffect.SetInput(0, listImages[nImage]);

                            SetEffectFloat(pBlurEffect, (uint)D2D1_GAUSSIANBLUR_PROP.D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION, nStandardDeviationGaussianBlur);

                            D2DTools.SetInputEffect(pBlurEffect, 0, pAffineTransformEffect);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pBlurEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nStandardDeviationGaussianBlur += nBlurStep;
                            if (nStandardDeviationGaussianBlur >= 500 && nBlurStep > 0)
                            {
                                nBlurStep = -nBlurStep;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                            }
                            if (nBlurStep < 0 && nStandardDeviationGaussianBlur <= 0)
                            {
                                nBlurStep = -nBlurStep;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pBlurEffect);
                            SafeRelease(ref pAffineTransformEffect);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_CROP)
                        {
                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            ID2D1Effect pCropEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Crop, out pCropEffect);
                            pCropEffect.SetInput(0, listImages[nImage]);

                            float[] aFloatArray = { nCropX, nCropY, size.width - nCropX, size.height - nCropY };
                            SetEffectFloatArray(pCropEffect, (uint)D2D1_CROP_PROP.D2D1_CROP_PROP_RECT, aFloatArray);

                            D2DTools.SetInputEffect(pCropEffect, 0, pAffineTransformEffect);

                            ID2D1Effect pCompositeEffect;
                            m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Composite, out pCompositeEffect);

                            ID2D1Effect pAffineTransformEffect2 = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect2);
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;
                            pAffineTransformEffect2.SetInput(0, listImages[nImage2]);

                            listImages[nImage2].GetSize(out D2D1_SIZE_F bmpSize2);
                            var scaleMatrix2 = Matrix3x2F.Scale(size.width / bmpSize2.width, size.height / bmpSize2.height);

                            float[] aFloatArray2 = {scaleMatrix2._11, scaleMatrix2._12,
                                scaleMatrix2._21, scaleMatrix2._22,
                                scaleMatrix2._31, scaleMatrix2._32
                            };

                            SetEffectFloatArray(pAffineTransformEffect2, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray2);

                            D2DTools.SetInputEffect(pCompositeEffect, 0, pAffineTransformEffect2);
                            D2DTools.SetInputEffect(pCompositeEffect, 1, pCropEffect);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pCompositeEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nCropX += 5;
                            nCropY += 5;
                            if (nCropX >= size.width / 2 || nCropY >= size.height / 2)
                            {
                                nCropX = 0;
                                nCropY = 0;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pCompositeEffect);
                            SafeRelease(ref pAffineTransformEffect2);
                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pCropEffect);
                            SafeRelease(ref pAffineTransformEffect);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_BRIGHTNESS)
                        {
                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            ID2D1Effect pBrightnessEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Brightness, out pBrightnessEffect);
                            //pBrightnessEffect.SetInput(0, listImages[nImage]);

                            D2DTools.SetInputEffect(pBrightnessEffect, 0, pAffineTransformEffect);

                            float[] aFloatArray = { 1.0f, nBrightnessWhiteY };
                            SetEffectFloatArray(pBrightnessEffect, (uint)D2D1_BRIGHTNESS_PROP.D2D1_BRIGHTNESS_PROP_WHITE_POINT, aFloatArray);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pBrightnessEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nBrightnessWhiteY += nBrightnessWhiteYStep;
                            if (nBrightnessWhiteY <= 0 && nBrightnessWhiteYStep < 0)
                            {
                                nBrightnessWhiteYStep = -nBrightnessWhiteYStep;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                            }
                            if (nBrightnessWhiteY >= 1 && nBrightnessWhiteYStep >= 0)
                            {
                                nBrightnessWhiteYStep = -nBrightnessWhiteYStep;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pBrightnessEffect);
                            SafeRelease(ref pAffineTransformEffect);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_ROTATE)
                        {
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;

                            ID2D1Effect pAffineTransformEffect2 = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect2);
                            pAffineTransformEffect2.SetInput(0, listImages[nImage2]);

                            listImages[nImage2].GetSize(out D2D1_SIZE_F bmpSize2);
                            nRotationScaleX += (size.width / bmpSize2.width) / 360.0f;
                            nRotationScaleY += (size.height / bmpSize2.height) / 360.0f;
                            var scaleMatrix = Matrix3x2F.Scale(nRotationScaleX, nRotationScaleY);
                            var rotateMatrix = Matrix3x2F.Rotation(nRotationAngle, new D2D1_POINT_2F(size.width / 2, size.height / 2));
                            var translateMatrix = Matrix3x2F.Translation(new D2D1_SIZE_F((size.width - (bmpSize2.width * nRotationScaleX)) / 2.0f, (size.height - (bmpSize2.height * nRotationScaleY)) / 2.0f));
                            var resultMatrix = scaleMatrix * translateMatrix * rotateMatrix;

                            float[] aFloatArray2 = {resultMatrix._11, resultMatrix._12,
                                resultMatrix._21, resultMatrix._22,
                                resultMatrix._31, resultMatrix._32
                            };

                            SetEffectFloatArray(pAffineTransformEffect2, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray2);

                            ID2D1Effect pCompositeEffect;
                            m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Composite, out pCompositeEffect);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix1 = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix1._11, scaleMatrix1._12,
                                scaleMatrix1._21, scaleMatrix1._22,
                                scaleMatrix1._31, scaleMatrix1._32
                            };
                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            D2DTools.SetInputEffect(pCompositeEffect, 0, pAffineTransformEffect);
                            D2DTools.SetInputEffect(pCompositeEffect, 1, pAffineTransformEffect2);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pCompositeEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nRotationAngle += 1.0f;
                            if (nRotationAngle >= 360)
                            {
                                nRotationAngle = 0.0f;
                                nRotationScaleX = 0.0f;
                                nRotationScaleY = 0.0f;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pAffineTransformEffect);
                            SafeRelease(ref pAffineTransformEffect2);
                            SafeRelease(ref pCompositeEffect);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_GRIDMASK)
                        {
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;

                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            hr = m_pD2DDeviceContext.EndDraw(out ulong tag11, out ulong tag12);

                            // Create mask with a grid of Ellipses (10x10)

                            listImages[nImage2].GetSize(out D2D1_SIZE_F sizeBitmapF);
                            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
                            sizeBitmapU.width = (uint)sizeBitmapF.width;
                            sizeBitmapU.height = (uint)sizeBitmapF.height;

                            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
                            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
                            bitmapProperties1.dpiX = 96;
                            bitmapProperties1.dpiY = 96;
                            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
                            ID2D1Bitmap1 pTargetBitmap1;
                            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);
                            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
                            m_pD2DDeviceContext.BeginDraw();
                            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black, 0));

                            ID2D1SolidColorBrush pBrush = null;
                            hr = m_pD2DDeviceContext.CreateSolidColorBrush(new ColorF(ColorF.Enum.Magenta, 1.0f), BrushProperties(), out pBrush);
                            float nRadiusOrigX = ((float)sizeBitmapU.width / 10.0f) / 2.0f;
                            float nRadiusOrigY = ((float)sizeBitmapU.height / 10.0f) / 2.0f;
                            for (uint nX = 0; nX <= sizeBitmapU.width; nX += sizeBitmapU.width / 10)
                            {
                                for (uint nY = 0; nY <= sizeBitmapU.height; nY += sizeBitmapU.height / 10)
                                {
                                    float nRadiusX = nRadiusOrigX/10 * nRadiusXStep;
                                    float nRadiusY = nRadiusOrigY/10 * nRadiusYStep;
                                    D2D1_POINT_2F ptf = new D2D1_POINT_2F(nX + nRadiusOrigX, nY + nRadiusOrigY);
                                    D2D1_ELLIPSE ellipse = new D2D1_ELLIPSE();
                                    ellipse.point = ptf;
                                    ellipse.radiusX = nRadiusX;
                                    ellipse.radiusY = nRadiusY;
                                    m_pD2DDeviceContext.FillEllipse(ref ellipse, pBrush);
                                }
                            }
                            SafeRelease(ref pBrush);

                            nRadiusXStep += 0.1f;
                            nRadiusYStep += 0.1f;
                            if (nRadiusOrigX / 10 * nRadiusXStep >= nRadiusOrigX + nRadiusOrigX / 3.0f)
                            {
                                nRadiusXStep = 1.0f;
                                nRadiusYStep = 1.0f;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                                m_bAnimate = false;
                            }
                           
                            hr = m_pD2DDeviceContext.EndDraw(out ulong tag21, out ulong tag22);

                            ID2D1Effect pAlphaMaskEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1AlphaMask, out pAlphaMaskEffect);
                            pAlphaMaskEffect.SetInput(0, listImages[nImage2]);
                            pAlphaMaskEffect.SetInput(1, pTargetBitmap1);

                            ID2D1Effect pAffineTransformEffect2 = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect2);
                            //pAffineTransformEffect2.SetInput(0, listImages[nImage2]);

                            listImages[nImage2].GetSize(out D2D1_SIZE_F bmpSize2);
                            var scaleMatrix2 = Matrix3x2F.Scale(size.width / bmpSize2.width, size.height / bmpSize2.height);

                            float[] aFloatArray2 = {scaleMatrix2._11, scaleMatrix2._12,
                                scaleMatrix2._21, scaleMatrix2._22,
                                scaleMatrix2._31, scaleMatrix2._32
                            };

                            SetEffectFloatArray(pAffineTransformEffect2, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray2);

                            D2DTools.SetInputEffect(pAffineTransformEffect2, 0, pAlphaMaskEffect);

                            //SaveD2D1BitmapToFile(pTargetBitmap1, m_pD2DDeviceContext, "save.png");
                            SafeRelease(ref pTargetBitmap1);

                            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);
                            m_pD2DDeviceContext.BeginDraw();

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage1 = null;
                            pAffineTransformEffect.GetOutput(out pOutputImage1);
                            m_pD2DDeviceContext.DrawImage(pOutputImage1, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
                            SafeRelease(ref pOutputImage1);

                            ID2D1Image pOutputImage = null;
                            //pAffineTransformEffect.GetOutput(out pOutputImage);
                            pAffineTransformEffect2.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            // (300 - (30 * i))/2 = 270/2 = 135
                            //D2D1_RECT_F destRectangle = new D2D1_RECT_F();
                            //destRectangle.left = 0.0f;
                            //destRectangle.top = 0.0f;
                            //destRectangle.right = size.width * (size.width / bmpSize.width) / 10;
                            //destRectangle.bottom = size.height * (size.height / bmpSize.height) / 10;
                            ////D2D1_SIZE_F bmpSize2 = listImages[nImage2].GetSize();
                            //D2D1_RECT_F sourceRectangle = new D2D1_RECT_F();
                            ////sourceRectangle.right = (size.width / bmpSize2.width) / 10;
                            ////sourceRectangle.bottom = (size.height / bmpSize2.height) / 10;
                            //sourceRectangle.right = (bmpSize2.width) / 10;
                            //sourceRectangle.bottom = (bmpSize2.height) / 10;
                            //m_pD2DDeviceContext.DrawBitmap(listImages[nImage2], destRectangle, 1.0f, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, sourceRectangle);

                            SafeRelease(ref pAlphaMaskEffect);
                            SafeRelease(ref pAffineTransformEffect2);
                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pAffineTransformEffect);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_CHROMA_KEY)
                        {
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;

                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            ID2D1Effect pChromaKeyEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1ChromaKey, out pChromaKeyEffect);
                            pChromaKeyEffect.SetInput(0, listImages[nImage]);

                            // RGB                            
                            float[] aFloatArray = { 128.0f/ 255.0f, 128.0f / 255.0f, 128.0f / 255.0f };
                            SetEffectFloatArray(pChromaKeyEffect, (uint)D2D1_CHROMAKEY_PROP.D2D1_CHROMAKEY_PROP_COLOR, aFloatArray);

                            SetEffectFloat(pChromaKeyEffect, (uint)D2D1_CHROMAKEY_PROP.D2D1_CHROMAKEY_PROP_TOLERANCE, nChromaKeyTolerance);
                
                            D2DTools.SetInputEffect(pChromaKeyEffect, 0, pAffineTransformEffect);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pChromaKeyEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nChromaKeyTolerance += nChromaKeyToleranceStep;
                            if (nChromaKeyTolerance >= 1.0f && nChromaKeyToleranceStep >= 0)
                            {
                                nChromaKeyToleranceStep = -nChromaKeyToleranceStep;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                            }
                            if (nChromaKeyTolerance <= 0 && nChromaKeyToleranceStep < 0)
                            {
                                nChromaKeyToleranceStep = -nChromaKeyToleranceStep;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pAffineTransformEffect);
                            SafeRelease(ref pChromaKeyEffect);
                        }
                        // Commented in XAML, not great...
                        else if (_Animation == (uint)ANIMATION.ANIMATION_MORPHOLOGY)
                        {
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;

                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            ID2D1Effect pMorphologyEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Morphology, out pMorphologyEffect);
                            pMorphologyEffect.SetInput(0, listImages[nImage]);

                            SetEffectInt(pMorphologyEffect, (uint)D2D1_MORPHOLOGY_PROP.D2D1_MORPHOLOGY_PROP_WIDTH, (uint)nMorphologyWidth);
                            SetEffectInt(pMorphologyEffect, (uint)D2D1_MORPHOLOGY_PROP.D2D1_MORPHOLOGY_PROP_HEIGHT, (uint)nMorphologyHeight);

                            D2DTools.SetInputEffect(pMorphologyEffect, 0, pAffineTransformEffect);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pMorphologyEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nMorphologyWidth += nMorphologyStep;
                            nMorphologyHeight+= nMorphologyStep;
                            if (nMorphologyWidth >= 100.0f && nMorphologyStep >= 0)
                            {
                                nMorphologyStep = -nMorphologyStep;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                            }
                            if (nMorphologyWidth <= 1.0f && nMorphologyStep < 0)
                            {
                                nMorphologyStep = -nMorphologyStep;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pAffineTransformEffect);
                            SafeRelease(ref pMorphologyEffect);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_ZOOM)
                        {
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;

                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            ID2D1Effect pScaleEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Scale, out pScaleEffect);
                            pScaleEffect.SetInput(0, listImages[nImage]);

                            float[] aFloatArray = { nScaleX, nScaleY };
                            SetEffectFloatArray(pScaleEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_SCALE, aFloatArray);

                            float[] aFloatArray2 = { (bmpSize.width * (size.width / bmpSize.width))/2,
                                (bmpSize.height * (size.height / bmpSize.height))/2 };
                            SetEffectFloatArray(pScaleEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_CENTER_POINT, aFloatArray2);
                            D2DTools.SetInputEffect(pScaleEffect, 0, pAffineTransformEffect);

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pScaleEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nScaleX += nScaleStep;
                            nScaleY += nScaleStep;
                            if (nScaleX >= 20.0f && nScaleStep >= 0)
                            {
                                nScaleStep = -nScaleStep;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                            }
                            if (nScaleX <= 1.0f && nScaleStep < 0)
                            {
                                nScaleStep = -nScaleStep;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pAffineTransformEffect);
                            SafeRelease(ref pScaleEffect);
                        }
                        else if (_Animation == (uint)ANIMATION.ANIMATION_TURBULENCE)
                        {
                            int nImage2 = nImage + 1;
                            if (nImage2 >= listImages.Count)
                                nImage2 = 0;

                            ID2D1Effect pAffineTransformEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                            pAffineTransformEffect.SetInput(0, listImages[nImage]);

                            listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);
                            var scaleMatrix = Matrix3x2F.Scale(size.width / bmpSize.width, size.height / bmpSize.height);

                            float[] aFloatArray1 = {scaleMatrix._11, scaleMatrix._12,
                                scaleMatrix._21, scaleMatrix._22,
                                scaleMatrix._31, scaleMatrix._32
                            };
                            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray1);

                            ID2D1Effect pDisplacementMapEffect = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1DisplacementMap, out pDisplacementMapEffect);
                            //pDisplacementMapEffect.SetInput(0, listImages[nImage]);

                            SetEffectFloat(pDisplacementMapEffect, (uint)D2D1_DISPLACEMENTMAP_PROP.D2D1_DISPLACEMENTMAP_PROP_SCALE, nDisplacementMapScale);
                           
                            SetEffectInt(pDisplacementMapEffect, (uint)D2D1_DISPLACEMENTMAP_PROP.D2D1_DISPLACEMENTMAP_PROP_X_CHANNEL_SELECT, (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_B);
                            SetEffectInt(pDisplacementMapEffect, (uint)D2D1_DISPLACEMENTMAP_PROP.D2D1_DISPLACEMENTMAP_PROP_Y_CHANNEL_SELECT, (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_B);

                            ID2D1Effect pEffectTurbulence = null;
                            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Turbulence, out pEffectTurbulence);
                           
                            float[] aFloatArray = { 0.20f, 0.20f };
                            SetEffectFloatArray(pEffectTurbulence, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_BASE_FREQUENCY, aFloatArray);

                            D2DTools.SetInputEffect(pDisplacementMapEffect, 0, pAffineTransformEffect);

                            float[] aFloatArray2 = { (bmpSize.width * (size.width / bmpSize.width)), 
                               (bmpSize.height * (size.height / bmpSize.height)) };
                            SetEffectFloatArray(pEffectTurbulence, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_SIZE, aFloatArray2);

                            D2DTools.SetInputEffect(pDisplacementMapEffect, 1, pEffectTurbulence);                        

                            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                            imageRectangle.left = 0.0f;
                            imageRectangle.top = 0.0f;
                            imageRectangle.right = size.width;
                            imageRectangle.bottom = size.height;

                            ID2D1Image pOutputImage = null;
                            pDisplacementMapEffect.GetOutput(out pOutputImage);
                            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                            nDisplacementMapScale *= nDisplacementMapScaleStep;
                            //if (nDisplacementMapScale >= 2000.0f && nDisplacementMapScaleStep >= 0)
                            if (nDisplacementMapScale >= 2500.0f)
                            {
                                //nDisplacementMapScaleStep = -nDisplacementMapScaleStep;
                                nDisplacementMapScaleStep = 1.0f/nDisplacementMapScaleStep;
                                nImage += 1;
                                if (nImage >= listImages.Count)
                                    nImage = 0;
                            }
                            //if (nDisplacementMapScale <= 0.0f && nDisplacementMapScaleStep < 0)
                            if (nDisplacementMapScale <= 1.01f)
                            {
                                //nDisplacementMapScaleStep = -nDisplacementMapScaleStep;
                                nDisplacementMapScaleStep = 1.0f / nDisplacementMapScaleStep;
                                m_bAnimate = false;
                            }

                            SafeRelease(ref pOutputImage);
                            SafeRelease(ref pAffineTransformEffect);
                            SafeRelease(ref pDisplacementMapEffect);
                            SafeRelease(ref pEffectTurbulence);
                        }
                    }
                }

                hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
                hr = m_pDXGISwapChain1.Present(1, 0);
            }
            return (hr);
        }

        HRESULT RenderOld()
        {
            HRESULT hr = HRESULT.S_OK;
            if (m_pD2DDeviceContext != null)
            {
                m_pD2DDeviceContext.BeginDraw();
                m_pD2DDeviceContext.GetSize(out D2D1_SIZE_F size);

                if (!m_bAnimate)
                { 
                    listImages[nImage].GetSize(out D2D1_SIZE_F sizeBmpBackground);
                    D2D1_RECT_F destRectBackground = new D2D1_RECT_F(0.0f, 0.0f, size.width, size.height);
                    D2D1_RECT_F sourceRectBackground = new D2D1_RECT_F(0.0f, 0.0f, sizeBmpBackground.width, sizeBmpBackground.height);
                    m_pD2DDeviceContext.DrawBitmap(listImages[nImage], ref destRectBackground, 1.0f, D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, ref sourceRectBackground);
                }
                else
                {
                    m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));
                    if (listImages[nImage] != null)
                    { 
                        D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
                        D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
                        imageRectangle.left = 0.0f;
                        imageRectangle.top = 0.0f;
                        listImages[nImage].GetSize(out D2D1_SIZE_F bmpSize);

                        var scaleMatrix = Matrix3x2F.Scale(size.width/bmpSize.width, size.height/bmpSize.height);
                        imageRectangle.right = imageRectangle.left + bmpSize.width * (size.width / bmpSize.width);
                        imageRectangle.bottom = imageRectangle.top + bmpSize.height * (size.height / bmpSize.height);

                        ID2D1Effect pAffineTransformEffect = null;
                        hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
                        pAffineTransformEffect.SetInput(0, listImages[nImage]);                     

                        float[] aFloatArray = {scaleMatrix._11, scaleMatrix._12,
                            scaleMatrix._21, scaleMatrix._22,
                            -nXTranslate, scaleMatrix._32
                        };                        
                        //_11 1.100141    float
                        //_22 0.7708565   float
                        SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray);

                        ID2D1Image pOutputImage = null;
                        pAffineTransformEffect.GetOutput(out pOutputImage);

                        m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

                        nXTranslate *= 1.025f;
                        if (nXTranslate >= size.width)
                        {
                            nXTranslate = 1;
                            nImage += 1;
                            if (nImage >= listImages.Count)
                                nImage = 0;
                            m_bAnimate = false;
                        }

                        SafeRelease(ref pOutputImage);                        
                        SafeRelease(ref pAffineTransformEffect);
                    }                                   
                }
              
                hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
                hr = m_pDXGISwapChain1.Present(1, 0);
            }
            return (hr);
        }

        private void ImgEffect_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            //Pointer pt = e.Pointer;

            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Point-Diffuse lighting")
            {
                Image img = (Image)sender;
                Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(img);
                m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
                double nWidth = img.ActualWidth;
                double nHeight = img.ActualHeight;
                float fRatioX = (float)nWidth / sizeBitmapF.width;
                float fRatioY = (float)nHeight / sizeBitmapF.height;
                _PointDiffuseLightingX = (float)point.Position.X / fRatioX;
                _PointDiffuseLightingY = (float)point.Position.Y / fRatioY;
                EffectPointDiffuseLighting();
            }
            else if (m_pD2DBitmap != null && sEffect == " Point-Specular lighting")
            {
                Image img = (Image)sender;
                Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(img);
                m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
                double nWidth = img.ActualWidth;
                double nHeight = img.ActualHeight;
                float fRatioX = (float)nWidth / sizeBitmapF.width;
                float fRatioY = (float)nHeight / sizeBitmapF.height;
                _PointSpecularLightingX = (float)point.Position.X / fRatioX;
                _PointSpecularLightingY = (float)point.Position.Y / fRatioY;
                EffectPointSpecularLighting();
            }
            else if (m_pD2DBitmap != null && sEffect == " Spot-Diffuse lighting")
            {
                Image img = (Image)sender;
                Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(img);
                m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
                double nWidth = img.ActualWidth;
                double nHeight = img.ActualHeight;
                float fRatioX = (float)nWidth / sizeBitmapF.width;
                float fRatioY = (float)nHeight / sizeBitmapF.height;
                _SpotDiffuseLightingX = (float)point.Position.X / fRatioX;
                _SpotDiffuseLightingY = (float)point.Position.Y / fRatioY;
                EffectSpotDiffuseLighting();
            }
            else if (m_pD2DBitmap != null && sEffect == " Spot-Specular lighting")
            {
                Image img = (Image)sender;
                Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(img);
                m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
                double nWidth = img.ActualWidth;
                double nHeight = img.ActualHeight;
                float fRatioX = (float)nWidth / sizeBitmapF.width;
                float fRatioY = (float)nHeight / sizeBitmapF.height;
                _SpotSpecularLightingX = (float)point.Position.X / fRatioX;
                _SpotSpecularLightingY = (float)point.Position.Y / fRatioY;
                EffectSpotSpecularLighting();
            }
        }

        private void EffectGaussianBlur()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            //ID2D1Effect embossEffect = null;
            //HRESULT hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Emboss, out embossEffect);
            ////SetValue(index, D2D1_PROPERTY_TYPE_UNKNOWN, data, dataSize);
            //embossEffect.SetInput(0, m_pD2DBitmap);
            //IntPtr pEmboss = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IntPtr)));
            //float[] f = { 1.0f };
            //Marshal.Copy(f, 0, pEmboss, 1);
            //hr = embossEffect.SetValue((uint)D2D1_EMBOSS_PROP.D2D1_EMBOSS_PROP_HEIGHT, D2D1_PROPERTY_TYPE.D2D1_PROPERTY_TYPE_UNKNOWN, pEmboss, (uint)Marshal.SizeOf(typeof(IntPtr)));
            //Marshal.FreeHGlobal(pEmboss);

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1GaussianBlur, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_GAUSSIANBLUR_PROP.D2D1_GAUSSIANBLUR_PROP_STANDARD_DEVIATION, _StandardDeviationGaussianBlur);
            SetEffectInt(pEffect, (uint)D2D1_GAUSSIANBLUR_PROP.D2D1_GAUSSIANBLUR_PROP_OPTIMIZATION, _OptimizationGaussianBlur);
            SetEffectInt(pEffect, (uint)D2D1_GAUSSIANBLUR_PROP.D2D1_GAUSSIANBLUR_PROP_BORDER_MODE, _BorderModeGaussianBlur);

            ID2D1Image pOutputImage = null;
            //embossEffect.GetOutput(out output);
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);
            
            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectDirectionalBlur()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1DirectionalBlur, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_DIRECTIONALBLUR_PROP.D2D1_DIRECTIONALBLUR_PROP_STANDARD_DEVIATION, _StandardDeviationDirectionalBlur);
            SetEffectFloat(pEffect, (uint)D2D1_DIRECTIONALBLUR_PROP.D2D1_DIRECTIONALBLUR_PROP_ANGLE, _AngleDirectionalBlur);
            SetEffectInt(pEffect, (uint)D2D1_DIRECTIONALBLUR_PROP.D2D1_DIRECTIONALBLUR_PROP_OPTIMIZATION, _OptimizationDirectionalBlur);
            SetEffectInt(pEffect, (uint)D2D1_DIRECTIONALBLUR_PROP.D2D1_DIRECTIONALBLUR_PROP_BORDER_MODE, _BorderModeDirectionalBlur);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectEdgeDetection()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1EdgeDetection, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_EDGEDETECTION_PROP.D2D1_EDGEDETECTION_PROP_STRENGTH, _StrengthEdgeDetection);
            SetEffectFloat(pEffect, (uint)D2D1_EDGEDETECTION_PROP.D2D1_EDGEDETECTION_PROP_BLUR_RADIUS, _BlurRadiusEdgeDetection);
            SetEffectInt(pEffect, (uint)D2D1_EDGEDETECTION_PROP.D2D1_EDGEDETECTION_PROP_MODE, _ModeEdgeDetection);
            SetEffectInt(pEffect, (uint)D2D1_EDGEDETECTION_PROP.D2D1_EDGEDETECTION_PROP_OVERLAY_EDGES, (uint)(tsOverlay_Edges.IsOn ? 1 : 0));
            //SetEffectInt(pEffect, (uint)D2D1_EDGEDETECTION_PROP.D2D1_EDGEDETECTION_PROP_ALPHA_MODE, _AlphaModeEdgeDetection);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectMorphology()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Morphology, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectInt(pEffect, (uint)D2D1_MORPHOLOGY_PROP.D2D1_MORPHOLOGY_PROP_MODE, _ModeMorphology);
            SetEffectInt(pEffect, (uint)D2D1_MORPHOLOGY_PROP.D2D1_MORPHOLOGY_PROP_WIDTH, (uint)_WidthMorphology);
            SetEffectInt(pEffect, (uint)D2D1_MORPHOLOGY_PROP.D2D1_MORPHOLOGY_PROP_HEIGHT, (uint)_HeightMorphology);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectDiscreteTransfer()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1DiscreteTransfer, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            //float[] aFloatArray = { 0.0f, 0.5f, 1.0f };
            //float[] aFloatArray = { 0.25f, 0.5f, 0.75f, 1.0f };

            float[] aFloatArrayRed = {
                (double.IsNaN(nbDiscreteTransferRed1.Value))?0:(float)nbDiscreteTransferRed1.Value,
                (double.IsNaN(nbDiscreteTransferRed2.Value))?0:(float)nbDiscreteTransferRed2.Value,
                (double.IsNaN(nbDiscreteTransferRed3.Value))?0:(float)nbDiscreteTransferRed3.Value,
                (double.IsNaN(nbDiscreteTransferRed4.Value))?0:(float)nbDiscreteTransferRed4.Value,
                (double.IsNaN(nbDiscreteTransferRed5.Value))?0:(float)nbDiscreteTransferRed5.Value,
            };

            SetEffectFloatArray(pEffect, (uint)D2D1_DISCRETETRANSFER_PROP.D2D1_DISCRETETRANSFER_PROP_RED_TABLE, aFloatArrayRed);
            // Does not work ?
            //SetEffectInt(pEffect, (uint)D2D1_DISCRETETRANSFER_PROP.D2D1_DISCRETETRANSFER_PROP_RED_DISABLE, (uint)(tsDiscreteTransferRed.IsOn ? 0 : 1));

            float[] aFloatArrayGreen = {
                (double.IsNaN(nbDiscreteTransferGreen1.Value))?0:(float)nbDiscreteTransferGreen1.Value,
                (double.IsNaN(nbDiscreteTransferGreen2.Value))?0:(float)nbDiscreteTransferGreen2.Value,
                (double.IsNaN(nbDiscreteTransferGreen3.Value))?0:(float)nbDiscreteTransferGreen3.Value,
                (double.IsNaN(nbDiscreteTransferGreen4.Value))?0:(float)nbDiscreteTransferGreen4.Value,
                (double.IsNaN(nbDiscreteTransferGreen5.Value))?0:(float)nbDiscreteTransferGreen5.Value,
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_DISCRETETRANSFER_PROP.D2D1_DISCRETETRANSFER_PROP_GREEN_TABLE, aFloatArrayGreen);
            //SetEffectInt(pEffect, (uint)D2D1_DISCRETETRANSFER_PROP.D2D1_DISCRETETRANSFER_PROP_GREEN_DISABLE, (uint)(tsDiscreteTransferGreen.IsOn ? 0 : 1));

            float[] aFloatArrayBlue = {
                (double.IsNaN(nbDiscreteTransferBlue1.Value))?0:(float)nbDiscreteTransferBlue1.Value,
                (double.IsNaN(nbDiscreteTransferBlue2.Value))?0:(float)nbDiscreteTransferBlue2.Value,
                (double.IsNaN(nbDiscreteTransferBlue3.Value))?0:(float)nbDiscreteTransferBlue3.Value,
                (double.IsNaN(nbDiscreteTransferBlue4.Value))?0:(float)nbDiscreteTransferBlue4.Value,
                (double.IsNaN(nbDiscreteTransferBlue5.Value))?0:(float)nbDiscreteTransferBlue5.Value,
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_DISCRETETRANSFER_PROP.D2D1_DISCRETETRANSFER_PROP_BLUE_TABLE, aFloatArrayBlue);
            //SetEffectInt(pEffect, (uint)D2D1_DISCRETETRANSFER_PROP.D2D1_DISCRETETRANSFER_PROP_BLUE_DISABLE, (uint)(tsDiscreteTransferBlue.IsOn ? 0 : 1));

            //SetEffectFloatArray(pEffect, (uint)D2D1_DISCRETETRANSFER_PROP.D2D1_DISCRETETRANSFER_PROP_ALPHA_TABLE, aFloatArrayAlpha);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectTableTransfer()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1TableTransfer, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            float[] aFloatArrayRed = {
               (double.IsNaN(nbTableTransferRed1.Value))?0:(float)nbTableTransferRed1.Value,
               (double.IsNaN(nbTableTransferRed2.Value))?0:(float)nbTableTransferRed2.Value,
               (double.IsNaN(nbTableTransferRed3.Value))?0:(float)nbTableTransferRed3.Value,
               (double.IsNaN(nbTableTransferRed4.Value))?0:(float)nbTableTransferRed4.Value,
               (double.IsNaN(nbTableTransferRed5.Value))?0:(float)nbTableTransferRed5.Value,
            };

            SetEffectFloatArray(pEffect, (uint)D2D1_TABLETRANSFER_PROP.D2D1_TABLETRANSFER_PROP_RED_TABLE, aFloatArrayRed);
            SetEffectInt(pEffect, (uint)D2D1_TABLETRANSFER_PROP.D2D1_TABLETRANSFER_PROP_RED_DISABLE, (uint)(tsTableTransferRed.IsOn ? 0 : 1));

            float[] aFloatArrayGreen = {
               (double.IsNaN(nbTableTransferGreen1.Value))?0:(float)nbTableTransferGreen1.Value,
               (double.IsNaN(nbTableTransferGreen2.Value))?0:(float)nbTableTransferGreen2.Value,
               (double.IsNaN(nbTableTransferGreen3.Value))?0:(float)nbTableTransferGreen3.Value,
               (double.IsNaN(nbTableTransferGreen4.Value))?0:(float)nbTableTransferGreen4.Value,
               (double.IsNaN(nbTableTransferGreen5.Value))?0:(float)nbTableTransferGreen5.Value,
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_TABLETRANSFER_PROP.D2D1_TABLETRANSFER_PROP_GREEN_TABLE, aFloatArrayGreen);
            SetEffectInt(pEffect, (uint)D2D1_TABLETRANSFER_PROP.D2D1_TABLETRANSFER_PROP_GREEN_DISABLE, (uint)(tsTableTransferGreen.IsOn ? 0 : 1));

            float[] aFloatArrayBlue = {
               (double.IsNaN(nbTableTransferBlue1.Value))?0:(float)nbTableTransferBlue1.Value,
               (double.IsNaN(nbTableTransferBlue2.Value))?0:(float)nbTableTransferBlue2.Value,
               (double.IsNaN(nbTableTransferBlue3.Value))?0:(float)nbTableTransferBlue3.Value,
               (double.IsNaN(nbTableTransferBlue4.Value))?0:(float)nbTableTransferBlue4.Value,
               (double.IsNaN(nbTableTransferBlue5.Value))?0:(float)nbTableTransferBlue5.Value,
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_TABLETRANSFER_PROP.D2D1_TABLETRANSFER_PROP_BLUE_TABLE, aFloatArrayBlue);
            SetEffectInt(pEffect, (uint)D2D1_TABLETRANSFER_PROP.D2D1_TABLETRANSFER_PROP_BLUE_DISABLE, (uint)(tsTableTransferBlue.IsOn ? 0 : 1));

            //SetEffectFloatArray(pEffect, (uint)D2D1_TableTRANSFER_PROP.D2D1_TableTRANSFER_PROP_ALPHA_TABLE, aFloatArrayAlpha);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectHueToRGB()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1HueToRgb, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectInt(pEffect, (uint)D2D1_HUETORGB_PROP.D2D1_HUETORGB_PROP_INPUT_COLOR_SPACE, (uint)_InputColorSpaceHueToRGB);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectRGBToHue()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1RgbToHue, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectInt(pEffect, (uint)D2D1_RGBTOHUE_PROP.D2D1_RGBTOHUE_PROP_OUTPUT_COLOR_SPACE, (uint)_OutputColorSpaceRGBToHue);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectSaturation()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Saturation, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_SATURATION_PROP.D2D1_SATURATION_PROP_SATURATION, _Saturation);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectGammaTransfer()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1GammaTransfer, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_RED_AMPLITUDE, _RedAmplitude);
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_RED_EXPONENT, _RedExponent);
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_RED_OFFSET, _RedOffset);
            SetEffectInt(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_RED_DISABLE, (uint)(tsRed.IsOn ? 0 : 1));
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_GREEN_AMPLITUDE, _GreenAmplitude);
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_GREEN_EXPONENT, _GreenExponent);
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_GREEN_OFFSET, _GreenOffset);
            SetEffectInt(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_GREEN_DISABLE, (uint)(tsGreen.IsOn ? 0 : 1));
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_BLUE_AMPLITUDE, _BlueAmplitude);
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_BLUE_EXPONENT, _BlueExponent);
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_BLUE_OFFSET, _BlueOffset);
            SetEffectInt(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_BLUE_DISABLE, (uint)(tsBlue.IsOn ? 0 : 1));
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_ALPHA_AMPLITUDE, _AlphaAmplitude);
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_ALPHA_EXPONENT, _AlphaExponent);
            SetEffectFloat(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_ALPHA_OFFSET, _AlphaOffset);
            SetEffectInt(pEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_ALPHA_DISABLE, (uint)(tsAlpha.IsOn ? 0 : 1));

            //SetEffectInt(pGammaTransferEffect, (uint)D2D1_GAMMATRANSFER_PROP.D2D1_GAMMATRANSFER_PROP_CLAMP_OUTPUT, (uint)(tsClampOutput.IsOn ? 0 : 1));

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectConvolveMatrix()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1ConvolveMatrix, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectInt(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_KERNEL_SIZE_X, (uint)5);
            SetEffectInt(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_KERNEL_SIZE_Y, (uint)5);

            // "https://en.wikipedia.org/wiki/Kernel_(image_processing)"
            //https://docs.gimp.org/2.8/en/plug-in-convmatrix.html

            //float[] aFloatArray = { -1, -1, -1, -1, 9, -1, -1, -1, -1 };

            // Box Blur
            //float[] aFloatArray = { (float)1 / 9, (float)1 / 9, (float)1 / 9, 
            //    (float)1 / 9, (float)1 / 9, (float)1 / 9,
            //    (float)1 / 9,  (float)1 / 9, (float) 1 / 9 };

            // Gaussian Blur
            //float[] aFloatArray = {(float)1 / 256, (float)4 / 256, (float)6 / 256, (float)4 / 256, (float)1 / 256,
            //    (float)4 / 256, (float)16 / 256, (float)24 / 256, (float)16 / 256, (float)4 / 256,
            //    (float)6 / 256, (float)24 / 256, (float)36 / 256, (float)24 / 256, (float)6 / 256,
            //    (float)4 / 256, (float)16 / 256, (float)24 / 256, (float)16 / 256, (float)4 / 256,
            //    (float)1 / 256, (float)4 / 256, (float)6 / 256, (float)4 / 256, (float)1 / 256};
            //SetEffectInt(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_KERNEL_SIZE_X, (uint)5);
            //SetEffectInt(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_KERNEL_SIZE_Y, (uint)5);

            // Sharpen
            //float[] aFloatArray = {0, -0.5f, 0, -0.5f, 3, -0.5f, 0, -0.5f, 0 };

            float[] aFloatArray = {
                (double.IsNaN(nb1.Value))?0:(float)nb1.Value, (double.IsNaN(nb2.Value))?0:(float)nb2.Value, (double.IsNaN(nb3.Value))?0:(float)nb3.Value, (double.IsNaN(nb4.Value))?0:(float)nb4.Value, (double.IsNaN(nb5.Value))?0:(float)nb5.Value,
 (double.IsNaN(nb6.Value))?0:(float)nb6.Value, (double.IsNaN(nb7.Value))?0:(float)nb7.Value, (double.IsNaN(nb8.Value))?0:(float)nb8.Value, (double.IsNaN(nb9.Value))?0:(float)nb9.Value, (double.IsNaN(nb10.Value))?0:(float)nb10.Value,
 (double.IsNaN(nb11.Value))?0:(float)nb11.Value, (double.IsNaN(nb12.Value))?0:(float)nb12.Value, (double.IsNaN(nb13.Value))?0:(float)nb13.Value, (double.IsNaN(nb14.Value))?0:(float)nb14.Value, (double.IsNaN(nb15.Value))?0:(float)nb15.Value,
 (double.IsNaN(nb16.Value))?0:(float)nb16.Value, (double.IsNaN(nb17.Value))?0:(float)nb17.Value, (double.IsNaN(nb18.Value))?0:(float)nb18.Value, (double.IsNaN(nb19.Value))?0:(float)nb19.Value, (double.IsNaN(nb20.Value))?0:(float)nb20.Value,
 (double.IsNaN(nb21.Value))?0:(float)nb21.Value, (double.IsNaN(nb22.Value))?0:(float)nb22.Value, (double.IsNaN(nb23.Value))?0:(float)nb23.Value, (double.IsNaN(nb24.Value))?0:(float)nb24.Value, (double.IsNaN(nb25.Value))?0:(float)nb25.Value,
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_KERNEL_MATRIX, aFloatArray);

            //float[] aFloatArray2 = { 0, 1 };
            //SetEffectFloatArray(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_KERNEL_UNIT_LENGTH, aFloatArray2);

            //float[] aFloatArray2 = { 0, 1 };
            //SetEffectFloatArray(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_KERNEL_OFFSET, aFloatArray2);

            SetEffectInt(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_BORDER_MODE, _BorderModeMatrix);
            SetEffectInt(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_SCALE_MODE, _ScaleModeMatrix);
            SetEffectFloat(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_DIVISOR, _DivisorMatrix);
            SetEffectFloat(pEffect, (uint)D2D1_CONVOLVEMATRIX_PROP.D2D1_CONVOLVEMATRIX_PROP_BIAS, _BiasMatrix);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectColorMatrix()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1ColorMatrix, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            float[] aFloatArray = {
                (double.IsNaN(nbColorMatrix1.Value))?0:(float)nbColorMatrix1.Value, (double.IsNaN(nbColorMatrix2.Value))?0:(float)nbColorMatrix2.Value, (double.IsNaN(nbColorMatrix3.Value))?0:(float)nbColorMatrix3.Value, (double.IsNaN(nbColorMatrix4.Value))?0:(float)nbColorMatrix4.Value,
                (double.IsNaN(nbColorMatrix5.Value))?0:(float)nbColorMatrix5.Value, (double.IsNaN(nbColorMatrix6.Value))?0:(float)nbColorMatrix6.Value, (double.IsNaN(nbColorMatrix7.Value))?0:(float)nbColorMatrix7.Value, (double.IsNaN(nbColorMatrix8.Value))?0:(float)nbColorMatrix8.Value,
                (double.IsNaN(nbColorMatrix9.Value))?0:(float)nbColorMatrix9.Value, (double.IsNaN(nbColorMatrix10.Value))?0:(float)nbColorMatrix10.Value, (double.IsNaN(nbColorMatrix11.Value))?0:(float)nbColorMatrix11.Value, (double.IsNaN(nbColorMatrix12.Value))?0:(float)nbColorMatrix12.Value,
                (double.IsNaN(nbColorMatrix13.Value))?0:(float)nbColorMatrix13.Value, (double.IsNaN(nbColorMatrix14.Value))?0:(float)nbColorMatrix14.Value, (double.IsNaN(nbColorMatrix15.Value))?0:(float)nbColorMatrix15.Value, (double.IsNaN(nbColorMatrix16.Value))?0:(float)nbColorMatrix16.Value,
                (double.IsNaN(nbColorMatrix17.Value))?0:(float)nbColorMatrix17.Value, (double.IsNaN(nbColorMatrix18.Value))?0:(float)nbColorMatrix18.Value, (double.IsNaN(nbColorMatrix19.Value))?0:(float)nbColorMatrix19.Value, (double.IsNaN(nbColorMatrix20.Value))?0:(float)nbColorMatrix20.Value,
           };
            SetEffectFloatArray(pEffect, (uint)D2D1_COLORMATRIX_PROP.D2D1_COLORMATRIX_PROP_COLOR_MATRIX, aFloatArray);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectHueRotation()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1HueRotation, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_HUEROTATION_PROP.D2D1_HUEROTATION_PROP_ANGLE, _AngleHueRotation);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectLinearTransfer()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1LinearTransfer, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            // -1 to 1
            SetEffectFloat(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_RED_Y_INTERCEPT, _RedYInterceptLinearTransfer);
            // 0 to 100
            SetEffectFloat(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_RED_SLOPE, _RedSlopeLinearTransfer);
            SetEffectInt(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_RED_DISABLE, (uint)(tsLinearTransferRed.IsOn ? 0 : 1));

            SetEffectFloat(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_GREEN_Y_INTERCEPT, _GreenYInterceptLinearTransfer);
            SetEffectFloat(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_GREEN_SLOPE, _GreenSlopeLinearTransfer);
            SetEffectInt(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_GREEN_DISABLE, (uint)(tsLinearTransferGreen.IsOn ? 0 : 1));

            SetEffectFloat(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_BLUE_Y_INTERCEPT, _BlueYInterceptLinearTransfer);
            SetEffectFloat(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_BLUE_SLOPE, _BlueSlopeLinearTransfer);
            SetEffectInt(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_BLUE_DISABLE, (uint)(tsLinearTransferBlue.IsOn ? 0 : 1));

            SetEffectFloat(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_ALPHA_Y_INTERCEPT, _AlphaYInterceptLinearTransfer);
            SetEffectFloat(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_ALPHA_SLOPE, _AlphaSlopeLinearTransfer);
            SetEffectInt(pEffect, (uint)D2D1_LINEARTRANSFER_PROP.D2D1_LINEARTRANSFER_PROP_ALPHA_DISABLE, (uint)(tsLinearTransferAlpha.IsOn ? 0 : 1));

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectTint()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Tint, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            // RGBA
            //float[] aFloatArray = { 0, 0, 1, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_TintColor.R / 255.0f), (float)((float)_TintColor.G / 255.0f), (float)((float)_TintColor.B / 255.0f), 1.0f };
            SetEffectFloatArray(pEffect, (uint)D2D1_TINT_PROP.D2D1_TINT_PROP_COLOR, aFloatArray);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectDistantDiffuseLighting()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1DistantDiffuse, out pEffect);
            D2DTools.SetInputEffect(pEffect, 0, m_pBitmapSourceEffect);

            SetEffectFloat(pEffect, (uint)D2D1_DISTANTDIFFUSE_PROP.D2D1_DISTANTDIFFUSE_PROP_AZIMUTH, _DistantDiffuseLightingAzimuth);
            SetEffectFloat(pEffect, (uint)D2D1_DISTANTDIFFUSE_PROP.D2D1_DISTANTDIFFUSE_PROP_ELEVATION, _DistantDiffuseLightingElevation);

            // RGB
            //float[] aFloatArray = { 0, 0, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_DistantDiffuseColor.R / 255.0f), (float)((float)_DistantDiffuseColor.G / 255.0f), (float)((float)_DistantDiffuseColor.B / 255.0f) };
            SetEffectFloatArray(pEffect, (uint)D2D1_DISTANTDIFFUSE_PROP.D2D1_DISTANTDIFFUSE_PROP_COLOR, aFloatArray);

            SetEffectFloat(pEffect, (uint)D2D1_DISTANTDIFFUSE_PROP.D2D1_DISTANTDIFFUSE_PROP_DIFFUSE_CONSTANT, _DistantDiffuseLightingDiffuseConstant);
            SetEffectFloat(pEffect, (uint)D2D1_DISTANTDIFFUSE_PROP.D2D1_DISTANTDIFFUSE_PROP_SURFACE_SCALE, _DistantDiffuseLightingSurfaceScale);

            float[] aFloatArray2 = { 1, 1 };
            SetEffectFloatArray(pEffect, (uint)D2D1_DISTANTDIFFUSE_PROP.D2D1_DISTANTDIFFUSE_PROP_KERNEL_UNIT_LENGTH, aFloatArray2);

            SetEffectInt(pEffect, (uint)D2D1_DISTANTDIFFUSE_PROP.D2D1_DISTANTDIFFUSE_PROP_SCALE_MODE, (uint)_ScaleModeDistantDiffuse);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectDistantSpecularLighting()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1DistantSpecular, out pEffect);
            D2DTools.SetInputEffect(pEffect, 0, m_pBitmapSourceEffect);

            SetEffectFloat(pEffect, (uint)D2D1_DISTANTSPECULAR_PROP.D2D1_DISTANTSPECULAR_PROP_AZIMUTH, _DistantSpecularLightingAzimuth);
            SetEffectFloat(pEffect, (uint)D2D1_DISTANTSPECULAR_PROP.D2D1_DISTANTSPECULAR_PROP_ELEVATION, _DistantSpecularLightingElevation);

            // RGB
            //float[] aFloatArray = { 0, 0, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_DistantSpecularColor.R / 255.0f), (float)((float)_DistantSpecularColor.G / 255.0f), (float)((float)_DistantSpecularColor.B / 255.0f) };
            SetEffectFloatArray(pEffect, (uint)D2D1_DISTANTSPECULAR_PROP.D2D1_DISTANTSPECULAR_PROP_COLOR, aFloatArray);

            SetEffectFloat(pEffect, (uint)D2D1_DISTANTSPECULAR_PROP.D2D1_DISTANTSPECULAR_PROP_SPECULAR_CONSTANT, _DistantSpecularLightingSpecularConstant);
            SetEffectFloat(pEffect, (uint)D2D1_DISTANTSPECULAR_PROP.D2D1_DISTANTSPECULAR_PROP_SURFACE_SCALE, _DistantSpecularLightingSurfaceScale);
            SetEffectFloat(pEffect, (uint)D2D1_DISTANTSPECULAR_PROP.D2D1_DISTANTSPECULAR_PROP_SPECULAR_EXPONENT, _DistantSpecularLightingExponent);

            float[] aFloatArray2 = { 1, 1 };
            SetEffectFloatArray(pEffect, (uint)D2D1_DISTANTSPECULAR_PROP.D2D1_DISTANTSPECULAR_PROP_KERNEL_UNIT_LENGTH, aFloatArray2);

            SetEffectInt(pEffect, (uint)D2D1_DISTANTSPECULAR_PROP.D2D1_DISTANTSPECULAR_PROP_SCALE_MODE, (uint)_ScaleModeDistantSpecular);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectEmboss()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Emboss, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_EMBOSS_PROP.D2D1_EMBOSS_PROP_HEIGHT, _EmbossHeight);
            SetEffectFloat(pEffect, (uint)D2D1_EMBOSS_PROP.D2D1_EMBOSS_PROP_DIRECTION, _EmbossDirection);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectPointDiffuseLighting()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1PointDiffuse, out pEffect);
            D2DTools.SetInputEffect(pEffect, 0, m_pBitmapSourceEffect);

            // RGB
            //float[] aFloatArray = { 0, 0, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_PointDiffuseColor.R / 255.0f), (float)((float)_PointDiffuseColor.G / 255.0f), (float)((float)_PointDiffuseColor.B / 255.0f) };
            SetEffectFloatArray(pEffect, (uint)D2D1_POINTDIFFUSE_PROP.D2D1_POINTDIFFUSE_PROP_COLOR, aFloatArray);

            SetEffectFloat(pEffect, (uint)D2D1_POINTDIFFUSE_PROP.D2D1_POINTDIFFUSE_PROP_DIFFUSE_CONSTANT, _PointDiffuseLightingDiffuseConstant);
            SetEffectFloat(pEffect, (uint)D2D1_POINTDIFFUSE_PROP.D2D1_POINTDIFFUSE_PROP_SURFACE_SCALE, _PointDiffuseLightingSurfaceScale);

            float[] aFloatArray2 = { 1, 1 };
            SetEffectFloatArray(pEffect, (uint)D2D1_POINTDIFFUSE_PROP.D2D1_POINTDIFFUSE_PROP_KERNEL_UNIT_LENGTH, aFloatArray2);

            SetEffectInt(pEffect, (uint)D2D1_POINTDIFFUSE_PROP.D2D1_POINTDIFFUSE_PROP_SCALE_MODE, (uint)_ScaleModePointDiffuse);

            // Z-Axis 0-250
            float[] aFloatArray3 = { _PointDiffuseLightingX, _PointDiffuseLightingY, _PointDiffuseLightingZ };
            SetEffectFloatArray(pEffect, (uint)D2D1_POINTDIFFUSE_PROP.D2D1_POINTDIFFUSE_PROP_LIGHT_POSITION, aFloatArray3);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectPointSpecularLighting()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1PointSpecular, out pEffect);
            D2DTools.SetInputEffect(pEffect, 0, m_pBitmapSourceEffect);

            // RGB
            //float[] aFloatArray = { 0, 0, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_PointSpecularColor.R / 255.0f), (float)((float)_PointSpecularColor.G / 255.0f), (float)((float)_PointSpecularColor.B / 255.0f) };
            SetEffectFloatArray(pEffect, (uint)D2D1_POINTSPECULAR_PROP.D2D1_POINTSPECULAR_PROP_COLOR, aFloatArray);

            SetEffectFloat(pEffect, (uint)D2D1_POINTSPECULAR_PROP.D2D1_POINTSPECULAR_PROP_SPECULAR_EXPONENT, _PointSpecularLightingSpecularExponent);
            SetEffectFloat(pEffect, (uint)D2D1_POINTSPECULAR_PROP.D2D1_POINTSPECULAR_PROP_SPECULAR_CONSTANT, _PointSpecularLightingSpecularConstant);
            SetEffectFloat(pEffect, (uint)D2D1_POINTSPECULAR_PROP.D2D1_POINTSPECULAR_PROP_SURFACE_SCALE, _PointSpecularLightingSurfaceScale);

            float[] aFloatArray2 = { 1, 1 };
            SetEffectFloatArray(pEffect, (uint)D2D1_POINTSPECULAR_PROP.D2D1_POINTSPECULAR_PROP_KERNEL_UNIT_LENGTH, aFloatArray2);

            SetEffectInt(pEffect, (uint)D2D1_POINTSPECULAR_PROP.D2D1_POINTSPECULAR_PROP_SCALE_MODE, (uint)_ScaleModePointSpecular);

            // Z-Axis 0-250
            float[] aFloatArray3 = { _PointSpecularLightingX, _PointSpecularLightingY, _PointSpecularLightingZ };
            SetEffectFloatArray(pEffect, (uint)D2D1_POINTSPECULAR_PROP.D2D1_POINTSPECULAR_PROP_LIGHT_POSITION, aFloatArray3);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectPosterize()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Posterize, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectInt(pEffect, (uint)D2D1_POSTERIZE_PROP.D2D1_POSTERIZE_PROP_RED_VALUE_COUNT, (uint)_RedValueCount);
            SetEffectInt(pEffect, (uint)D2D1_POSTERIZE_PROP.D2D1_POSTERIZE_PROP_GREEN_VALUE_COUNT, (uint)_GreenValueCount);
            SetEffectInt(pEffect, (uint)D2D1_POSTERIZE_PROP.D2D1_POSTERIZE_PROP_BLUE_VALUE_COUNT, (uint)_BlueValueCount);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectShadow()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmapTransparent2.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Shadow, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmapTransparent2);

            ID2D1Effect pFloodEffect;
            m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Flood, out pFloodEffect);
            float[] aFloatArray1 = { 1.0f, 1.0f, 1.0f, 1.0f };
            //float[] aFloatArray = { (float)((float)_LuminanceToAlphaColor.R / 255.0f), (float)((float)_LuminanceToAlphaColor.G / 255.0f), (float)((float)_LuminanceToAlphaColor.B / 255.0f), 1.0f };
            SetEffectFloatArray(pFloodEffect, (uint)D2D1_FLOOD_PROP.D2D1_FLOOD_PROP_COLOR, aFloatArray1);

            ID2D1Effect pAffineTransformEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pAffineTransformEffect);
            D2DTools.SetInputEffect(pAffineTransformEffect, 0, pEffect);

            float[] aFloatArray2 = {1.0f, 0.0f,
                0.0f, 1.0f,
                _ShadowTranslate, _ShadowTranslate
            };
            SetEffectFloatArray(pAffineTransformEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray2);

            ID2D1Effect pCompositeEffect;
            m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Composite, out pCompositeEffect);

            D2DTools.SetInputEffect(pCompositeEffect, 0, pFloodEffect);
            D2DTools.SetInputEffect(pCompositeEffect, 1, pAffineTransformEffect);
            pCompositeEffect.SetInput(2, m_pD2DBitmapTransparent2);

            //SetEffectInt(pCompositeEffect, (uint)D2D1_COMPOSITE_PROP.D2D1_COMPOSITE_PROP_MODE, (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_ATOP);

            SetEffectFloat(pEffect, (uint)D2D1_SHADOW_PROP.D2D1_SHADOW_PROP_BLUR_STANDARD_DEVIATION, _ShadowBlurStandardDeviation);
            SetEffectInt(pEffect, (uint)D2D1_SHADOW_PROP.D2D1_SHADOW_PROP_OPTIMIZATION, _OptimizationShadow);

            // ARGB
            //float[] aFloatArray = { 0, 0, 1, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_ShadowColor.R / 255.0f), (float)((float)_ShadowColor.G / 255.0f), (float)((float)_ShadowColor.B / 255.0f), 1.0f };
            SetEffectFloatArray(pEffect, (uint)D2D1_SHADOW_PROP.D2D1_SHADOW_PROP_COLOR, aFloatArray);

            ID2D1Image pOutputImage = null;
            pCompositeEffect.GetOutput(out pOutputImage);

            ID2D1Effect pScaleEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Scale, out pScaleEffect);
            pScaleEffect.SetInput(0, pOutputImage);
            //float[] aFloatArrayScale = { 0.75f, 0.75f };
            float[] aFloatArrayScale = { 1.0f, 1.0f };
            //float[] aFloatArrayScale = { fRatioX, fRatioY };
            SetEffectFloatArray(pScaleEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_SCALE, aFloatArrayScale);
            D2DTools.SetInputEffect(pScaleEffect, 0, pCompositeEffect);

            ID2D1Image pOutputImageEffectScaled = null;
            pScaleEffect.GetOutput(out pOutputImageEffectScaled);

            ID2D1Effect pScaleEffectOrig = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Scale, out pScaleEffectOrig);
            pScaleEffectOrig.SetInput(0, m_pD2DBitmapTransparent2);
            SetEffectFloatArray(pScaleEffectOrig, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_SCALE, aFloatArrayScale);

            ID2D1Image pOutputImageOrig = null;
            pScaleEffectOrig.GetOutput(out pOutputImageOrig);

            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmapTransparent2.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;

            m_pD2DDeviceContext.DrawImage(pOutputImageEffectScaled, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            m_pD2DDeviceContext.DrawImage(pOutputImageOrig, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
            SafeRelease(ref pAffineTransformEffect);
            SafeRelease(ref pFloodEffect);
            SafeRelease(ref pCompositeEffect);
            SafeRelease(ref pOutputImageOrig);
            SafeRelease(ref pOutputImageEffectScaled);
            SafeRelease(ref pScaleEffect);
            SafeRelease(ref pScaleEffectOrig);
        }

        private void EffectSpotDiffuseLighting()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1SpotDiffuse, out pEffect);
            D2DTools.SetInputEffect(pEffect, 0, m_pBitmapSourceEffect);

            // RGB
            //float[] aFloatArray = { 0, 0, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_SpotDiffuseColor.R / 255.0f), (float)((float)_SpotDiffuseColor.G / 255.0f), (float)((float)_SpotDiffuseColor.B / 255.0f) };
            SetEffectFloatArray(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_COLOR, aFloatArray);

            SetEffectFloat(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_DIFFUSE_CONSTANT, _SpotDiffuseLightingDiffuseConstant);
            SetEffectFloat(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_SURFACE_SCALE, _SpotDiffuseLightingSurfaceScale);
            SetEffectFloat(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_FOCUS, _SpotDiffuseLightingFocus);
            SetEffectFloat(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_LIMITING_CONE_ANGLE, _SpotDiffuseLightingLimitingConeAngle);

            float[] aFloatArray2 = { 1, 1 };
            SetEffectFloatArray(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_KERNEL_UNIT_LENGTH, aFloatArray2);

            SetEffectInt(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_SCALE_MODE, (uint)_ScaleModeSpotDiffuse);

            // Z-Axis 0-250
            float[] aFloatArray3 = { _SpotDiffuseLightingX, _SpotDiffuseLightingY, _SpotDiffuseLightingZ };
            SetEffectFloatArray(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_LIGHT_POSITION, aFloatArray3);

            double nWidth = imgEffect.ActualWidth;
            double nHeight = imgEffect.ActualHeight;
            float fCenterX = (float)nWidth / 2.0f;
            float fCenterY = (float)nHeight / 2.0f;
            float[] aFloatArray4 = { fCenterX, fCenterY, 0.0f };
            SetEffectFloatArray(pEffect, (uint)D2D1_SPOTDIFFUSE_PROP.D2D1_SPOTDIFFUSE_PROP_POINTS_AT, aFloatArray4);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectSpotSpecularLighting()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1SpotSpecular, out pEffect);
            D2DTools.SetInputEffect(pEffect, 0, m_pBitmapSourceEffect);

            // RGB
            //float[] aFloatArray = { 0, 0, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_SpotSpecularColor.R / 255.0f), (float)((float)_SpotSpecularColor.G / 255.0f), (float)((float)_SpotSpecularColor.B / 255.0f) };
            SetEffectFloatArray(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_COLOR, aFloatArray);

            SetEffectFloat(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_SPECULAR_CONSTANT, _SpotSpecularLightingSpecularConstant);
            SetEffectFloat(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_SURFACE_SCALE, _SpotSpecularLightingSurfaceScale);
            SetEffectFloat(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_SPECULAR_EXPONENT, _SpotSpecularLightingSpecularExponent);
            SetEffectFloat(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_FOCUS, _SpotSpecularLightingFocus);
            SetEffectFloat(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_LIMITING_CONE_ANGLE, _SpotSpecularLightingLimitingConeAngle);

            float[] aFloatArray2 = { 1, 1 };
            SetEffectFloatArray(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_KERNEL_UNIT_LENGTH, aFloatArray2);

            SetEffectInt(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_SCALE_MODE, (uint)_ScaleModeSpotSpecular);

            // Z-Axis 0-250
            float[] aFloatArray3 = { _SpotSpecularLightingX, _SpotSpecularLightingY, _SpotSpecularLightingZ };
            SetEffectFloatArray(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_LIGHT_POSITION, aFloatArray3);

            double nWidth = imgEffect.ActualWidth;
            double nHeight = imgEffect.ActualHeight;
            float fCenterX = (float)nWidth / 2.0f;
            float fCenterY = (float)nHeight / 2.0f;
            float[] aFloatArray4 = { fCenterX, fCenterY, 0.0f };
            SetEffectFloatArray(pEffect, (uint)D2D1_SPOTSPECULAR_PROP.D2D1_SPOTSPECULAR_PROP_POINTS_AT, aFloatArray4);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectBrightness()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Brightness, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            float[] aFloatArray = { _BrightnessWhitePointX, _BrightnessWhitePointY };
            SetEffectFloatArray(pEffect, (uint)D2D1_BRIGHTNESS_PROP.D2D1_BRIGHTNESS_PROP_WHITE_POINT, aFloatArray);
            float[] aFloatArray2 = { _BrightnessBlackPointX, _BrightnessBlackPointY };
            SetEffectFloatArray(pEffect, (uint)D2D1_BRIGHTNESS_PROP.D2D1_BRIGHTNESS_PROP_BLACK_POINT, aFloatArray2);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectContrast()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Contrast, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_CONTRAST_PROP.D2D1_CONTRAST_PROP_CONTRAST, _Contrast);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectExposure()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Exposure, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_EXPOSURE_PROP.D2D1_EXPOSURE_PROP_EXPOSURE_VALUE, _Exposure);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectGrayscale()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Grayscale, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectHighlightsAndShadows()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1HighlightsShadows, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_HIGHLIGHTSANDSHADOWS_PROP.D2D1_HIGHLIGHTSANDSHADOWS_PROP_HIGHLIGHTS, _HighlightsAndShadowsHighlights);
            SetEffectFloat(pEffect, (uint)D2D1_HIGHLIGHTSANDSHADOWS_PROP.D2D1_HIGHLIGHTSANDSHADOWS_PROP_SHADOWS, _HighlightsAndShadowsShadows);
            SetEffectFloat(pEffect, (uint)D2D1_HIGHLIGHTSANDSHADOWS_PROP.D2D1_HIGHLIGHTSANDSHADOWS_PROP_CLARITY, _HighlightsAndShadowsClarity);

            SetEffectFloat(pEffect, (uint)D2D1_HIGHLIGHTSANDSHADOWS_PROP.D2D1_HIGHLIGHTSANDSHADOWS_PROP_MASK_BLUR_RADIUS, _HighlightsAndShadowsMaskBlurRadius);
            SetEffectFloat(pEffect, (uint)D2D1_HIGHLIGHTSANDSHADOWS_PROP.D2D1_HIGHLIGHTSANDSHADOWS_PROP_INPUT_GAMMA, _HighlightsAndShadowsMaskInputGamma);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectInvert()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Invert, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectSepia()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Sepia, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_SEPIA_PROP.D2D1_SEPIA_PROP_INTENSITY, _SepiaIntensity);
            SetEffectInt(pEffect, (uint)D2D1_SEPIA_PROP.D2D1_SEPIA_PROP_ALPHA_MODE, (int)D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectSharpen()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Sharpen, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_SHARPEN_PROP.D2D1_SHARPEN_PROP_SHARPNESS, _SharpenSharpness);
            SetEffectFloat(pEffect, (uint)D2D1_SHARPEN_PROP.D2D1_SHARPEN_PROP_THRESHOLD, _SharpenThreshold);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectStraighten()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Straighten, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_STRAIGHTEN_PROP.D2D1_STRAIGHTEN_PROP_ANGLE, _StraightenAngle);
            SetEffectInt(pEffect, (uint)D2D1_STRAIGHTEN_PROP.D2D1_STRAIGHTEN_PROP_SCALE_MODE, _ScaleModeStraighten);
            SetEffectInt(pEffect, (uint)D2D1_STRAIGHTEN_PROP.D2D1_STRAIGHTEN_PROP_MAINTAIN_SIZE, (uint)(tsStraightenMaintainSize.IsOn ? 1 : 0));

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectTemperatureAndTint()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1TemperatureTint, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_TEMPERATUREANDTINT_PROP.D2D1_TEMPERATUREANDTINT_PROP_TEMPERATURE, _TemperatureAndTintTemperature);
            SetEffectFloat(pEffect, (uint)D2D1_TEMPERATUREANDTINT_PROP.D2D1_TEMPERATUREANDTINT_PROP_TINT, _TemperatureAndTintTint);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectVignette()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Vignette, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            // RGBA
            //float[] aFloatArray = { 0, 0, 1, 1 }; // Blue
            float[] aFloatArray = { (float)((float)_VignetteColor.R / 255.0f), (float)((float)_VignetteColor.G / 255.0f), (float)((float)_VignetteColor.B / 255.0f), 1.0f };
            SetEffectFloatArray(pEffect, (uint)D2D1_VIGNETTE_PROP.D2D1_VIGNETTE_PROP_COLOR, aFloatArray);

            SetEffectFloat(pEffect, (uint)D2D1_VIGNETTE_PROP.D2D1_VIGNETTE_PROP_TRANSITION_SIZE, _VignetteTransitionSize);
            SetEffectFloat(pEffect, (uint)D2D1_VIGNETTE_PROP.D2D1_VIGNETTE_PROP_STRENGTH, _VignetteStrength);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectAffineTransform()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D12DAffineTransform, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            float[] aFloatArray = {
               (double.IsNaN(nbAffineTransform1.Value))?0:(float)nbAffineTransform1.Value, (double.IsNaN(nbAffineTransform2.Value))?0:(float)nbAffineTransform2.Value,
               (double.IsNaN(nbAffineTransform3.Value))?0:(float)nbAffineTransform3.Value, (double.IsNaN(nbAffineTransform4.Value))?0:(float)nbAffineTransform4.Value,
               (double.IsNaN(nbAffineTransform5.Value))?0:(float)nbAffineTransform5.Value, (double.IsNaN(nbAffineTransform6.Value))?0:(float)nbAffineTransform6.Value };
            SetEffectFloatArray(pEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray);

            SetEffectInt(pEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_BORDER_MODE, _BorderModeAffineTransform);
            SetEffectInt(pEffect, (uint)D2D1_2DAFFINETRANSFORM_PROP.D2D1_2DAFFINETRANSFORM_PROP_INTERPOLATION_MODE, _InterpolationModeAffineTransform);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectTransform()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D13DTransform, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            float[] aFloatArray = {
                (double.IsNaN(nbTransform1.Value))?0:(float)nbTransform1.Value, (double.IsNaN(nbTransform2.Value))?0:(float)nbTransform2.Value,
                (double.IsNaN(nbTransform3.Value))?0:(float)nbTransform3.Value, (double.IsNaN(nbTransform4.Value))?0:(float)nbTransform4.Value,
                (double.IsNaN(nbTransform5.Value))?0:(float)nbTransform5.Value, (double.IsNaN(nbTransform6.Value))?0:(float)nbTransform6.Value,
                (double.IsNaN(nbTransform7.Value))?0:(float)nbTransform7.Value, (double.IsNaN(nbTransform8.Value))?0:(float)nbTransform8.Value,
                (double.IsNaN(nbTransform9.Value))?0:(float)nbTransform9.Value, (double.IsNaN(nbTransform10.Value))?0:(float)nbTransform10.Value,
                (double.IsNaN(nbTransform11.Value))?0:(float)nbTransform11.Value, (double.IsNaN(nbTransform12.Value))?0:(float)nbTransform12.Value,
                (double.IsNaN(nbTransform13.Value))?0:(float)nbTransform13.Value, (double.IsNaN(nbTransform14.Value))?0:(float)nbTransform14.Value,
                (double.IsNaN(nbTransform15.Value))?0:(float)nbTransform15.Value, (double.IsNaN(nbTransform16.Value))?0:(float)nbTransform16.Value};
            SetEffectFloatArray(pEffect, (uint)D2D1_3DTRANSFORM_PROP.D2D1_3DTRANSFORM_PROP_TRANSFORM_MATRIX, aFloatArray);

            SetEffectInt(pEffect, (uint)D2D1_3DTRANSFORM_PROP.D2D1_3DTRANSFORM_PROP_BORDER_MODE, _BorderModeTransform);
            SetEffectInt(pEffect, (uint)D2D1_3DTRANSFORM_PROP.D2D1_3DTRANSFORM_PROP_INTERPOLATION_MODE, _InterpolationModeTransform);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectPerspectiveTransform()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D13DPerspectiveTransform, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_DEPTH, (float)nbPerspectiveTransformDepth.Value);
            float[] aFloatArray = {
                (double.IsNaN(nbPerspectiveTransformPerspectiveOriginX.Value))?0:(float)nbPerspectiveTransformPerspectiveOriginX.Value,
                (double.IsNaN(nbPerspectiveTransformPerspectiveOriginY.Value))?0:(float)nbPerspectiveTransformPerspectiveOriginY.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_PERSPECTIVE_ORIGIN, aFloatArray);
            float[] aFloatArray2 = {
                (double.IsNaN(nbPerspectiveTransformLocalOffsetX.Value))?0:(float)nbPerspectiveTransformLocalOffsetX.Value,
                (double.IsNaN(nbPerspectiveTransformLocalOffsetY.Value))?0:(float)nbPerspectiveTransformLocalOffsetY.Value,
                (double.IsNaN(nbPerspectiveTransformLocalOffsetZ.Value))?0:(float)nbPerspectiveTransformLocalOffsetZ.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_LOCAL_OFFSET, aFloatArray2);
            float[] aFloatArray3 = {
                (double.IsNaN(nbPerspectiveTransformGlobalOffsetX.Value))?0:(float)nbPerspectiveTransformGlobalOffsetX.Value,
                (double.IsNaN(nbPerspectiveTransformGlobalOffsetY.Value))?0:(float)nbPerspectiveTransformGlobalOffsetY.Value,
                (double.IsNaN(nbPerspectiveTransformGlobalOffsetZ.Value))?0:(float)nbPerspectiveTransformGlobalOffsetZ.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_GLOBAL_OFFSET, aFloatArray3);
            float[] aFloatArray4 = {
                (double.IsNaN(nbPerspectiveTransformRotationOriginX.Value))?0:(float)nbPerspectiveTransformRotationOriginX.Value,
                (double.IsNaN(nbPerspectiveTransformRotationOriginY.Value))?0:(float)nbPerspectiveTransformRotationOriginY.Value,
                (double.IsNaN(nbPerspectiveTransformRotationOriginZ.Value))?0:(float)nbPerspectiveTransformRotationOriginZ.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_ROTATION_ORIGIN, aFloatArray4);
            float[] aFloatArray5 = {_PerspectiveTransformRotationAngleX, _PerspectiveTransformRotationAngleY, _PerspectiveTransformRotationAngleZ
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_ROTATION, aFloatArray5);

            SetEffectInt(pEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_BORDER_MODE, _BorderModePerspectiveTransform);
            SetEffectInt(pEffect, (uint)D2D1_3DPERSPECTIVETRANSFORM_PROP.D2D1_3DPERSPECTIVETRANSFORM_PROP_INTERPOLATION_MODE, _InterpolationModePerspectiveTransform);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectAtlas()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));
            //m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black, 0));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Atlas, out pEffect);
            //pEffect.SetInput(0, m_pD2DBitmap);
            D2DTools.SetInputEffect(pEffect, 0, m_pBitmapSourceEffect);

            float[] aFloatArray = {
                (double.IsNaN(nbAtlasInputRect1.Value))?0:(float)nbAtlasInputRect1.Value, (double.IsNaN(nbAtlasInputRect2.Value))?0:(float)nbAtlasInputRect2.Value,
                (double.IsNaN(nbAtlasInputRect3.Value))?0:(float)nbAtlasInputRect3.Value, (double.IsNaN(nbAtlasInputRect4.Value))?0:(float)nbAtlasInputRect4.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_ATLAS_PROP.D2D1_ATLAS_PROP_INPUT_RECT, aFloatArray);

            //float[] aFloatArray2 = {
            //    (double.IsNaN(nbAtlasInputPaddingRect1.Value))?0:(float)nbAtlasInputPaddingRect1.Value, (double.IsNaN(nbAtlasInputPaddingRect2.Value))?0:(float)nbAtlasInputPaddingRect2.Value,
            //    (double.IsNaN(nbAtlasInputPaddingRect3.Value))?0:(float)nbAtlasInputPaddingRect3.Value, (double.IsNaN(nbAtlasInputPaddingRect4.Value))?0:(float)nbAtlasInputPaddingRect4.Value
            //};
            //SetEffectFloatArray(pEffect, (uint)D2D1_ATLAS_PROP.D2D1_ATLAS_PROP_INPUT_PADDING_RECT, aFloatArray2);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectBorder()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Border, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectInt(pEffect, (uint)D2D1_BORDER_PROP.D2D1_BORDER_PROP_EDGE_MODE_X, (uint)_BorderEdgeModeX);
            SetEffectInt(pEffect, (uint)D2D1_BORDER_PROP.D2D1_BORDER_PROP_EDGE_MODE_Y, (uint)_BorderEdgeModeY);

            ID2D1Effect pScaleEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Scale, out pScaleEffect);
            pScaleEffect.SetInput(0, m_pD2DBitmap);
            float[] aFloatArray = { 0.5f, 0.5f };
            SetEffectFloatArray(pScaleEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_SCALE, aFloatArray);
            D2DTools.SetInputEffect(pEffect, 0, pScaleEffect);
            SafeRelease(ref pScaleEffect);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectCrop()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Crop, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            float[] aFloatArray = {
                (double.IsNaN(nbCropRect1.Value))?0:(float)nbCropRect1.Value, (double.IsNaN(nbCropRect2.Value))?0:(float)nbCropRect2.Value,
                (double.IsNaN(nbCropRect3.Value))?0:(float)nbCropRect3.Value, (double.IsNaN(nbCropRect4.Value))?0:(float)nbCropRect4.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_CROP_PROP.D2D1_CROP_PROP_RECT, aFloatArray);
            SetEffectInt(pEffect, (uint)D2D1_CROP_PROP.D2D1_CROP_PROP_BORDER_MODE, _BorderModeCrop);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectScale()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Scale, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            float[] aFloatArray = {
               _ScaleScaleX, _ScaleScaleY};
            SetEffectFloatArray(pEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_SCALE, aFloatArray);

            float[] aFloatArray2 = {
                (double.IsNaN(nbScaleCenterPointX.Value))?0:(float)nbScaleCenterPointX.Value, (double.IsNaN(nbScaleCenterPointY.Value))?0:(float)nbScaleCenterPointY.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_CENTER_POINT, aFloatArray2);

            SetEffectFloat(pEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_SHARPNESS, _ScaleSharpness);
            SetEffectInt(pEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_BORDER_MODE, _BorderModeScale);
            SetEffectInt(pEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_INTERPOLATION_MODE, _InterpolationModeScale);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectTile()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Tile, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            float[] aFloatArray = {
                (double.IsNaN(nbTileRect1.Value))?0:(float)nbTileRect1.Value, (double.IsNaN(nbTileRect2.Value))?0:(float)nbTileRect2.Value,
                (double.IsNaN(nbTileRect3.Value))?0:(float)nbTileRect3.Value, (double.IsNaN(nbTileRect4.Value))?0:(float)nbTileRect4.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_TILE_PROP.D2D1_TILE_PROP_RECT, aFloatArray);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectChromaKey()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1ChromaKey, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            // RGB
            //float[] aFloatArray = { 0, 0.5f, 0 }; // Green
            float[] aFloatArray = { (float)((float)_ChromaKeyColor.R / 255.0f), (float)((float)_ChromaKeyColor.G / 255.0f), (float)((float)_ChromaKeyColor.B / 255.0f) };
            SetEffectFloatArray(pEffect, (uint)D2D1_CHROMAKEY_PROP.D2D1_CHROMAKEY_PROP_COLOR, aFloatArray);

            SetEffectFloat(pEffect, (uint)D2D1_CHROMAKEY_PROP.D2D1_CHROMAKEY_PROP_TOLERANCE, _ChromaKeyTolerance);
            SetEffectInt(pEffect, (uint)D2D1_CHROMAKEY_PROP.D2D1_CHROMAKEY_PROP_INVERT_ALPHA, (uint)(tsChromaKeyInvertAlpha.IsOn ? 1 : 0));
            SetEffectInt(pEffect, (uint)D2D1_CHROMAKEY_PROP.D2D1_CHROMAKEY_PROP_FEATHER, (uint)(tsChromaKeyFeather.IsOn ? 1 : 0));

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectLuminanceToAlpha()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1LuminanceToAlpha, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            ID2D1Effect pFloodEffect;
            m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Flood, out pFloodEffect);
            //float[] aFloatArray = { 1.0f, 1.0f, 1.0f, 1.0f };
            float[] aFloatArray = { (float)((float)_LuminanceToAlphaColor.R / 255.0f), (float)((float)_LuminanceToAlphaColor.G / 255.0f), (float)((float)_LuminanceToAlphaColor.B / 255.0f), 1.0f };
            SetEffectFloatArray(pFloodEffect, (uint)D2D1_FLOOD_PROP.D2D1_FLOOD_PROP_COLOR, aFloatArray);

            ID2D1Effect pCompositeEffect;
            m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Composite, out pCompositeEffect);

            D2DTools.SetInputEffect(pCompositeEffect, 0, pFloodEffect);
            D2DTools.SetInputEffect(pCompositeEffect, 1, pEffect);

            ID2D1Image pOutputImage = null;
            pCompositeEffect.GetOutput(out pOutputImage);

            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pCompositeEffect);
            SafeRelease(ref pFloodEffect);
            SafeRelease(ref pEffect);
        }

        private void EffectOpacity()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Opacity, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_OPACITY_PROP.D2D1_OPACITY_PROP_OPACITY, _OpacityOpacity);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectAlphaMask()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1AlphaMask, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);
            pEffect.SetInput(1, m_pD2DBitmapMask);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            imageRectangle.right = imageRectangle.left + sizeBitmapF.width;
            imageRectangle.bottom = imageRectangle.top + sizeBitmapF.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);

            //SafeRelease(ref m_pD2DBitmap1);
        }

        private void EffectArithmeticComposite()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1ArithmeticComposite, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            //IWICBitmapSource pWICBitmapSource = null;
            //ID2D1Bitmap m_pD2DBitmap1 = null;
            //hr = LoadBitmapFromFile(m_pD2DDeviceContext3, m_pWICImagingFactory, "E:\\Sources\\WinUI3_Direct2D_Effects\\Assets\\Brown_Rabbit.jpg",
            //    sizeBitmapU.width, sizeBitmapU.height, out m_pD2DBitmap1, out pWICBitmapSource);
            //SafeRelease(ref pWICBitmapSource);

            pEffect.SetInput(1, m_pD2DBitmap1);

            float[] aFloatArray = {
                (double.IsNaN(nbC1.Value))?0:(float)nbC1.Value, (double.IsNaN(nbC2.Value))?0:(float)nbC2.Value,
                (double.IsNaN(nbC3.Value))?0:(float)nbC3.Value, (double.IsNaN(nbC4.Value))?0:(float)nbC4.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_ARITHMETICCOMPOSITE_PROP.D2D1_ARITHMETICCOMPOSITE_PROP_COEFFICIENTS, aFloatArray);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            imageRectangle.right = imageRectangle.left + sizeBitmapF.width;
            imageRectangle.bottom = imageRectangle.top + sizeBitmapF.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectBlend()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Blend, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            pEffect.SetInput(1, m_pD2DBitmap1);

            SetEffectInt(pEffect, (uint)D2D1_BLEND_PROP.D2D1_BLEND_PROP_MODE, _BlendModeBlend);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectComposite()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmapTransparent1.GetSize(out D2D1_SIZE_F sizeBitmapTransparentF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapTransparentF.width;
            sizeBitmapU.height = (uint)sizeBitmapTransparentF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black, 0));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Composite, out pEffect);
            pEffect.SetInput(1, m_pD2DBitmapTransparent1);

            pEffect.SetInput(0, m_pD2DBitmapTransparent2);

            SetEffectInt(pEffect, (uint)D2D1_COMPOSITE_PROP.D2D1_COMPOSITE_PROP_MODE, _CompositeModeComposite);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);

            ID2D1Effect pScaleEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Scale, out pScaleEffect);
            pScaleEffect.SetInput(0, pOutputImage);

            //D2D1_SIZE_F sizeBitmapTransparentF = m_pD2DBitmapTransparent1.GetSize();
            double nWidth = imgOrig.ActualWidth;
            double nHeight = imgOrig.ActualHeight;
            float fRatioX = (float)nWidth / sizeBitmapTransparentF.width;
            float fRatioY = (float)nHeight / sizeBitmapTransparentF.height;

            // float[] aFloatArray = { 0.5f, 0.5f };
            float[] aFloatArray = { fRatioX, fRatioY };
            SetEffectFloatArray(pScaleEffect, (uint)D2D1_SCALE_PROP.D2D1_SCALE_PROP_SCALE, aFloatArray);
            D2DTools.SetInputEffect(pScaleEffect, 0, pEffect);

            ID2D1Image pOutputImageScaled = null;
            pScaleEffect.GetOutput(out pOutputImageScaled);

            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmapTransparent1.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            //m_pD2DDeviceContext.DrawImage(pOutputImageScaled, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            //SaveD2D1BitmapToFile(pTargetBitmap1, m_pD2DDeviceContext, "E:\\save.png");
            // SaveD2D1BitmapToFile(m_pD2DBitmapEffect, m_pD2DDeviceContext, "E:\\save.png");

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            //imgEffect.Stretch = Stretch.UniformToFill;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
            SafeRelease(ref pScaleEffect);
            SafeRelease(ref pOutputImageScaled);
        }

        private void EffectCrossFade()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1CrossFade, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            pEffect.SetInput(1, m_pD2DBitmap1);

            SetEffectFloat(pEffect, (uint)D2D1_CROSSFADE_PROP.D2D1_CROSSFADE_PROP_WEIGHT, _CrossFadeWeight);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
        }

        private void EffectTurbulence()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Turbulence, out pEffect);
            //pEffect.SetInput(0, m_pD2DBitmap);

            ID2D1Effect pCompositeEffect;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Composite, out pCompositeEffect);
            pCompositeEffect.SetInput(0, m_pD2DBitmap);
            D2DTools.SetInputEffect(pCompositeEffect, 1, pEffect);

            //SetEffectInt(pCompositeEffect, (uint)D2D1_COMPOSITE_PROP.D2D1_COMPOSITE_PROP_MODE, (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_XOR);

            float[] aFloatArray = {
                (double.IsNaN(nbTurbulenceOffsetX.Value))?0:(float)nbTurbulenceOffsetX.Value,
                (double.IsNaN(nbTurbulenceOffsetY.Value))?0:(float)nbTurbulenceOffsetY.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_OFFSET, aFloatArray);

            float[] aFloatArray2 = {
                (double.IsNaN(nbTurbulenceSizeX.Value))?0:(float)nbTurbulenceSizeX.Value,
                (double.IsNaN(nbTurbulenceSizeY.Value))?0:(float)nbTurbulenceSizeY.Value
            };
            SetEffectFloatArray(pEffect, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_SIZE, aFloatArray2);

            float[] aFloatArray3 = { _TurbulenceBaseFrequencyX, TurbulenceBaseFrequencyY };
            SetEffectFloatArray(pEffect, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_BASE_FREQUENCY, aFloatArray3);

            SetEffectInt(pEffect, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_NUM_OCTAVES, (uint)_TurbulenceNumOctaves);
            SetEffectInt(pEffect, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_SEED, (uint)_TurbulenceSeed);

            SetEffectInt(pEffect, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_NOISE, (uint)_TurbulenceNoise);
            SetEffectInt(pEffect, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_STITCHABLE, (uint)(tsTurbulenceStitchable.IsOn ? 1 : 0));

            ID2D1Image pOutputImage = null;
            pCompositeEffect.GetOutput(out pOutputImage);

            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
            imageRectangle.right = imageRectangle.left + bmpSize.width;
            imageRectangle.bottom = imageRectangle.top + bmpSize.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
            SafeRelease(ref pCompositeEffect);
        }

        private void EffectDisplacementMap()
        {
            HRESULT hr = HRESULT.S_OK;

            m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
            D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
            sizeBitmapU.width = (uint)sizeBitmapF.width;
            sizeBitmapU.height = (uint)sizeBitmapF.height;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties1 = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties1.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            bitmapProperties1.dpiX = 96;
            bitmapProperties1.dpiY = 96;
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET;
            ID2D1Bitmap1 pTargetBitmap1;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out pTargetBitmap1);

            m_pD2DDeviceContext.SetTarget(pTargetBitmap1);
            m_pD2DDeviceContext.BeginDraw();
            m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Black));

            ID2D1Effect pEffect = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1DisplacementMap, out pEffect);
            pEffect.SetInput(0, m_pD2DBitmap);

            SetEffectFloat(pEffect, (uint)D2D1_DISPLACEMENTMAP_PROP.D2D1_DISPLACEMENTMAP_PROP_SCALE, _DisplacementMapScale);

            SetEffectInt(pEffect, (uint)D2D1_DISPLACEMENTMAP_PROP.D2D1_DISPLACEMENTMAP_PROP_X_CHANNEL_SELECT, _ChannelX);
            SetEffectInt(pEffect, (uint)D2D1_DISPLACEMENTMAP_PROP.D2D1_DISPLACEMENTMAP_PROP_Y_CHANNEL_SELECT, _ChannelY);

            ID2D1Effect pEffectTurbulence = null;
            hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Turbulence, out pEffectTurbulence);
            D2DTools.SetInputEffect(pEffect, 1, pEffectTurbulence);

            ////float[] aFloatArray = { 0.1f, 0.1f };
            //float[] aFloatArray = { 0.5f, 0.5f };
            float[] aFloatArray = { _TurbulenceBaseFrequencyDMX, _TurbulenceBaseFrequencyDMY };
            
            SetEffectFloatArray(pEffectTurbulence, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_BASE_FREQUENCY, aFloatArray);

            //SetEffectInt(pEffectTurbulence, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_NUM_OCTAVES, (uint)1);
            //SetEffectInt(pEffectTurbulence, (uint)D2D1_TURBULENCE_PROP.D2D1_TURBULENCE_PROP_SEED, (uint)1);

            //ID2D1Effect pFloodEffect;
            //m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1Flood, out pFloodEffect);
            //float[] aFloatArray1 = { 1.0f, 0.0f, 1.0f, 0.5f };
            ////float[] aFloatArray = { (float)((float)_LuminanceToAlphaColor.R / 255.0f), (float)((float)_LuminanceToAlphaColor.G / 255.0f), (float)((float)_LuminanceToAlphaColor.B / 255.0f), 1.0f };
            //SetEffectFloatArray(pFloodEffect, (uint)D2D1_FLOOD_PROP.D2D1_FLOOD_PROP_COLOR, aFloatArray1);
            ////D2DTools.SetInputEffect(pEffect, 1, pFloodEffect);
            //pEffect.SetInput(1, m_pD2DBitmapTransparent1);
            //SafeRelease(ref pFloodEffect);

            ID2D1Image pOutputImage = null;
            pEffect.GetOutput(out pOutputImage);
            D2D1_POINT_2F pt = new D2D1_POINT_2F(0, 0);
            D2D1_RECT_F imageRectangle = new D2D1_RECT_F();
            imageRectangle.left = 0.0f;
            imageRectangle.top = 0.0f;
            imageRectangle.right = imageRectangle.left + sizeBitmapF.width;
            imageRectangle.bottom = imageRectangle.top + sizeBitmapF.height;
            m_pD2DDeviceContext.DrawImage(pOutputImage, ref pt, ref imageRectangle, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR, D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);
            hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            hr = m_pDXGISwapChain1.Present(1, 0);
            m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);

            SafeRelease(ref m_pD2DBitmapEffect);
            bitmapProperties1.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_NONE;
            m_pD2DDeviceContext.CreateBitmap(sizeBitmapU, IntPtr.Zero, sizeBitmapU.width * 4, ref bitmapProperties1, out m_pD2DBitmapEffect);
            D2D1_POINT_2U pt1 = new D2D1_POINT_2U(0, 0);
            D2D1_RECT_U imageRectangle1 = new D2D1_RECT_U();
            imageRectangle1.left = 0;
            imageRectangle1.top = 0;
            imageRectangle1.right = (uint)imageRectangle.right;
            imageRectangle1.bottom = (uint)imageRectangle.bottom;
            m_pD2DBitmapEffect.CopyFromBitmap(ref pt1, pTargetBitmap1, imageRectangle1);

            ConvertD2D1BitmapToBitmapImage(pTargetBitmap1, m_pD2DDeviceContext, m_bitmapImageEffect);
            imgEffect.Source = m_bitmapImageEffect;
            SafeRelease(ref pTargetBitmap1);
            SafeRelease(ref pOutputImage);
            SafeRelease(ref pEffect);
            SafeRelease(ref pEffectTurbulence);
        }

        //

        private void SetEffectFloat(ID2D1Effect pEffect, uint nEffect, float fValue)
        {
            float[] aFloatArray = { fValue };
            int nDataSize = aFloatArray.Length * Marshal.SizeOf(typeof(float));
            IntPtr pData = Marshal.AllocHGlobal(nDataSize);
            Marshal.Copy(aFloatArray, 0, pData, aFloatArray.Length);
            HRESULT hr = pEffect.SetValue(nEffect, D2D1_PROPERTY_TYPE.D2D1_PROPERTY_TYPE_UNKNOWN, pData, (uint)nDataSize);
            Marshal.FreeHGlobal(pData);
        }

        private void SetEffectFloatArray(ID2D1Effect pEffect, uint nEffect, float[] aFloatArray)
        {
            int nDataSize = aFloatArray.Length * Marshal.SizeOf(typeof(float));
            IntPtr pData = Marshal.AllocHGlobal(nDataSize);
            Marshal.Copy(aFloatArray, 0, pData, aFloatArray.Length);
            HRESULT hr = pEffect.SetValue(nEffect, D2D1_PROPERTY_TYPE.D2D1_PROPERTY_TYPE_UNKNOWN, pData, (uint)nDataSize);
            Marshal.FreeHGlobal(pData);
        }

        private void SetEffectInt(ID2D1Effect pEffect, uint nEffect, uint nValue)
        {
            IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Int32)));
            Marshal.WriteInt32(pData, (int)nValue);
            HRESULT hr = pEffect.SetValue(nEffect, D2D1_PROPERTY_TYPE.D2D1_PROPERTY_TYPE_UNKNOWN, pData, (uint)Marshal.SizeOf(typeof(Int32)));
            Marshal.FreeHGlobal(pData);
        }

        private void SetEffectIntPtr(ID2D1Effect pEffect, uint nEffect, IntPtr pPointer)
        {
            IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IntPtr)));
            Marshal.WriteIntPtr(pData, pPointer);
            HRESULT hr = pEffect.SetValue(nEffect, D2D1_PROPERTY_TYPE.D2D1_PROPERTY_TYPE_UNKNOWN, pData, (uint)Marshal.SizeOf(typeof(IntPtr)));
            Marshal.FreeHGlobal(pData);
        }

        //

        HRESULT CreateD2D1Factory()
        {
            HRESULT hr = HRESULT.S_OK;
            D2D1_FACTORY_OPTIONS options = new D2D1_FACTORY_OPTIONS();

            // Needs "Enable native code Debugging"
            options.debugLevel = D2D1_DEBUG_LEVEL.D2D1_DEBUG_LEVEL_INFORMATION;

            hr = D2DTools.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, ref D2DTools.CLSID_D2D1Factory, ref options, out m_pD2DFactory);
            m_pD2DFactory1 = (ID2D1Factory1)m_pD2DFactory;
            return hr;
        }

        HRESULT CreateDeviceContext()
        {
            HRESULT hr = HRESULT.S_OK;
            uint creationFlags = (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;

            // Needs "Enable native code Debugging"
            creationFlags |= (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_DEBUG;

            int[] aD3D_FEATURE_LEVEL = new int[] { (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
                (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2, (int)D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1};

            D3D_FEATURE_LEVEL featureLevel;
            hr = D2DTools.D3D11CreateDevice(null,    // specify null to use the default adapter
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                creationFlags,              // optionally set debug and Direct2D compatibility flags
                                            //pD3D_FEATURE_LEVEL,              // list of feature levels this app can support
                aD3D_FEATURE_LEVEL,
                //(uint)Marshal.SizeOf(aD3D_FEATURE_LEVEL),   // number of possible feature levels
                (uint)aD3D_FEATURE_LEVEL.Length,
                D2DTools.D3D11_SDK_VERSION,
                out m_pD3D11DevicePtr,                    // returns the Direct3D device created
                out featureLevel,            // returns feature level of device created
                                             //out pD3D11DeviceContextPtr                    // returns the device immediate context
                out m_pD3D11DeviceContext
            );
            if (hr == HRESULT.S_OK)
            {
                //m_pD3D11DeviceContext = Marshal.GetObjectForIUnknown(pD3D11DeviceContextPtr) as ID3D11DeviceContext;             

                m_pDXGIDevice = Marshal.GetObjectForIUnknown(m_pD3D11DevicePtr) as IDXGIDevice1;
                if (m_pD2DFactory1 != null)
                {
                    hr = m_pD2DFactory1.CreateDevice(m_pDXGIDevice, out m_pD2DDevice);
                    if (hr == HRESULT.S_OK)
                    {
                        hr = m_pD2DDevice.CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS.D2D1_DEVICE_CONTEXT_OPTIONS_NONE, out m_pD2DDeviceContext);
                        SafeRelease(ref m_pD2DDevice);
                    }
                }
                //Marshal.ReleaseComObject(m_pDXGIDevice);
                Marshal.Release(m_pD3D11DevicePtr);
            }
            return hr;
        }

        HRESULT CreateSwapChain(IntPtr hWnd)
        {
            HRESULT hr = HRESULT.S_OK;
            DXGI_SWAP_CHAIN_DESC1 swapChainDesc = new DXGI_SWAP_CHAIN_DESC1();
            swapChainDesc.Width = 1;
            swapChainDesc.Height = 1;
            swapChainDesc.Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM; // this is the most common swapchain format
            swapChainDesc.Stereo = false;
            swapChainDesc.SampleDesc.Count = 1;                // don't use multi-sampling
            swapChainDesc.SampleDesc.Quality = 0;
            swapChainDesc.BufferUsage = D2DTools.DXGI_USAGE_RENDER_TARGET_OUTPUT;
            swapChainDesc.BufferCount = 2;                     // use double buffering to enable flip
            swapChainDesc.Scaling = (hWnd != IntPtr.Zero) ? DXGI_SCALING.DXGI_SCALING_NONE : DXGI_SCALING.DXGI_SCALING_STRETCH;
            swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL; // all apps must use this SwapEffect       
            swapChainDesc.Flags = 0;

            IDXGIAdapter pDXGIAdapter;
            hr = m_pDXGIDevice.GetAdapter(out pDXGIAdapter);
            if (hr == HRESULT.S_OK)
            {
                IntPtr pDXGIFactory2Ptr;
                hr = pDXGIAdapter.GetParent(typeof(IDXGIFactory2).GUID, out pDXGIFactory2Ptr);
                if (hr == HRESULT.S_OK)
                {
                    IDXGIFactory2 pDXGIFactory2 = Marshal.GetObjectForIUnknown(pDXGIFactory2Ptr) as IDXGIFactory2;
                    if (hWnd != IntPtr.Zero)
                        hr = pDXGIFactory2.CreateSwapChainForHwnd(m_pD3D11DevicePtr, hWnd, ref swapChainDesc, IntPtr.Zero, null, out m_pDXGISwapChain1);
                    else
                        hr = pDXGIFactory2.CreateSwapChainForComposition(m_pD3D11DevicePtr, ref swapChainDesc, null, out m_pDXGISwapChain1);

                    hr = m_pDXGIDevice.SetMaximumFrameLatency(1);
                    SafeRelease(ref pDXGIFactory2);
                    Marshal.Release(pDXGIFactory2Ptr);
                }
                SafeRelease(ref pDXGIAdapter);
            }
            return hr;
        }

        HRESULT ConfigureSwapChain()
        {
            HRESULT hr = HRESULT.S_OK;

            //IntPtr pD3D11Texture2DPtr = IntPtr.Zero;
            //hr = m_pDXGISwapChain1.GetBuffer(0, typeof(ID3D11Texture2D).GUID, ref pD3D11Texture2DPtr);
            //m_pD3D11Texture2D = Marshal.GetObjectForIUnknown(pD3D11Texture2DPtr) as ID3D11Texture2D;

            D2D1_BITMAP_PROPERTIES1 bitmapProperties = new D2D1_BITMAP_PROPERTIES1();
            bitmapProperties.bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CANNOT_DRAW;
            //bitmapProperties.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_IGNORE);
            bitmapProperties.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
            //float nDpiX, nDpiY = 0.0f;
            //m_pD2DContext.GetDpi(out nDpiX, out nDpiY);
            uint nDPI = GetDpiForWindow(hWndMain);
            bitmapProperties.dpiX = nDPI;
            bitmapProperties.dpiY = nDPI;

            IntPtr pDXGISurfacePtr = IntPtr.Zero;
            hr = m_pDXGISwapChain1.GetBuffer(0, typeof(IDXGISurface).GUID, out pDXGISurfacePtr);
            if (hr == HRESULT.S_OK)
            {
                IDXGISurface pDXGISurface = Marshal.GetObjectForIUnknown(pDXGISurfacePtr) as IDXGISurface;
                hr = m_pD2DDeviceContext.CreateBitmapFromDxgiSurface(pDXGISurface, ref bitmapProperties, out m_pD2DTargetBitmap);
                if (hr == HRESULT.S_OK)
                {
                    m_pD2DDeviceContext.SetTarget(m_pD2DTargetBitmap);
                }
                SafeRelease(ref pDXGISurface);
                Marshal.Release(pDXGISurfacePtr);
            }
            return hr;
        }

        HRESULT CreateDeviceResources()
        {
            HRESULT hr = HRESULT.S_OK;
            if (m_pD2DDeviceContext != null)
            {
                if (m_pD2DDeviceContext3 == null)
                    m_pD2DDeviceContext3 = (ID2D1DeviceContext3)m_pD2DDeviceContext;

                //if (m_pMainBrush == null)
                //    hr = m_pD2DDeviceContext.CreateSolidColorBrush(new ColorF(ColorF.Enum.Red), null, out m_pMainBrush);
              
                var imgSource = imgOrig.Source;
                string sExePath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string sAbsolutePath = ((Microsoft.UI.Xaml.Media.Imaging.BitmapImage)imgSource).UriSource.AbsolutePath;
                if (sAbsolutePath.StartsWith("/"))
                    sAbsolutePath = sExePath + sAbsolutePath;
                IWICBitmapSource pWICBitmapSource = null;
                hr = LoadBitmapFromFile(m_pD2DDeviceContext3, m_pWICImagingFactory, sAbsolutePath, 0, 0, out m_pD2DBitmap, out pWICBitmapSource);

                // pWICBitmapSource pas bon ?!
                //pf1 = {6fddc324-4e03-4bfe-b185-3d77768dc910} GUID_WICPixelFormat32bppPBGRA               
                pWICBitmapSource.GetPixelFormat(out Guid pf1);
                pWICBitmapSource.GetSize(out uint nW, out uint nH);
                // 72
                pWICBitmapSource.GetResolution(out double pX, out double pY);

                hr = m_pD2DDeviceContext.CreateEffect(D2DTools.CLSID_D2D1BitmapSource, out m_pBitmapSourceEffect);

                GetWICBitmapSourceFromD2D1Bitmap(m_pD2DBitmap, m_pD2DDeviceContext, out pWICBitmapSource);
                // pf2 = {6fddc324-4e03-4bfe-b185-3d77768dc90f} GUID_WICPixelFormat32bppBGRA 
                pWICBitmapSource.GetPixelFormat(out Guid pf2);
                pWICBitmapSource.GetSize(out uint nW2, out uint nH2);
                // 0
                pWICBitmapSource.GetResolution(out double pX2, out double pY2);

                IntPtr pWICBitmapSourcePtr = Marshal.GetComInterfaceForObject(pWICBitmapSource, typeof(IWICBitmapSource));
                SetEffectIntPtr(m_pBitmapSourceEffect, (uint)D2D1_BITMAPSOURCE_PROP.D2D1_BITMAPSOURCE_PROP_WIC_BITMAP_SOURCE, pWICBitmapSourcePtr);
                SetEffectInt(m_pBitmapSourceEffect, unchecked((uint)D2D1_PROPERTY.D2D1_PROPERTY_CACHED), 1);
                SetEffectInt(m_pBitmapSourceEffect, (uint)D2D1_BITMAPSOURCE_PROP.D2D1_BITMAPSOURCE_PROP_ALPHA_MODE, (uint)D2D1_BITMAPSOURCE_ALPHA_MODE.D2D1_BITMAPSOURCE_ALPHA_MODE_PREMULTIPLIED);

                SafeRelease(ref pWICBitmapSource);

                m_pD2DBitmap.GetSize(out D2D1_SIZE_F sizeBitmapF);
                D2D1_SIZE_U sizeBitmapU = new D2D1_SIZE_U();
                sizeBitmapU.width = (uint)sizeBitmapF.width;
                sizeBitmapU.height = (uint)sizeBitmapF.height;

                IWICBitmapSource pWICBitmapSource2 = null;
                var imgMaskSource = imgMask.Source;
                string sAbsolutePathMask = ((Microsoft.UI.Xaml.Media.Imaging.BitmapImage)imgMaskSource).UriSource.AbsolutePath;
                if (sAbsolutePathMask.StartsWith("/"))
                    sAbsolutePathMask = sExePath + sAbsolutePathMask;
                hr = LoadBitmapFromFile(m_pD2DDeviceContext3, m_pWICImagingFactory, sAbsolutePathMask,
                    sizeBitmapU.width, sizeBitmapU.height, out m_pD2DBitmapMask, out pWICBitmapSource2);
                SafeRelease(ref pWICBitmapSource2);

                IWICBitmapSource pWICBitmapSource3 = null;
                var img2Source = img2.Source;
                string sAbsolutePathImg2 = ((Microsoft.UI.Xaml.Media.Imaging.BitmapImage)img2Source).UriSource.AbsolutePath;
                if (sAbsolutePathImg2.StartsWith("/"))
                    sAbsolutePathImg2 = sExePath + sAbsolutePathImg2;
                hr = LoadBitmapFromFile(m_pD2DDeviceContext3, m_pWICImagingFactory, sAbsolutePathImg2,
                    sizeBitmapU.width, sizeBitmapU.height, out m_pD2DBitmap1, out pWICBitmapSource3);
                //hr = LoadBitmapFromFile(m_pD2DDeviceContext3, m_pWICImagingFactory, sAbsolutePathImg2,
                //  0, 0, out m_pD2DBitmap1, out pWICBitmapSource3);
                SafeRelease(ref pWICBitmapSource3);

                IWICBitmapSource pWICBitmapSource4 = null;
                string sAbsolutePathimgOrigTransparent = "/Assets/Butterfly_Brown_745.png";
                if (sAbsolutePathimgOrigTransparent.StartsWith("/"))
                    sAbsolutePathimgOrigTransparent = sExePath + sAbsolutePathimgOrigTransparent;
                hr = LoadBitmapFromFile(m_pD2DDeviceContext3, m_pWICImagingFactory, sAbsolutePathimgOrigTransparent,
                    0, 0, out m_pD2DBitmapTransparent1, out pWICBitmapSource4);
                SafeRelease(ref pWICBitmapSource4);

                IWICBitmapSource pWICBitmapSource5 = null;
                var img2TransparentSource = img2Transparent.Source;
                string sAbsolutePathimg2Transparent = ((Microsoft.UI.Xaml.Media.Imaging.BitmapImage)img2TransparentSource).UriSource.AbsolutePath;
                if (sAbsolutePathimg2Transparent.StartsWith("/"))
                    sAbsolutePathimg2Transparent = sExePath + sAbsolutePathimg2Transparent;
                hr = LoadBitmapFromFile(m_pD2DDeviceContext3, m_pWICImagingFactory, sAbsolutePathimg2Transparent,
                   0, 0, out m_pD2DBitmapTransparent2, out pWICBitmapSource5);
                SafeRelease(ref pWICBitmapSource5);

                List<string> strings = new List<string> {
                    sExePath + @"/Assets/Beach.jpg",
                    sExePath + @"/Assets/Beach2.jpg",
                    sExePath + @"/Assets/Island.jpg",
                    sExePath + @"/Assets/Sunset.jpg",
                    sExePath + @"/Assets/Sunset2.jpg",
                    sExePath + @"/Assets/Sunset3.jpg",
                    sExePath + @"/Assets/Paradise.jpg",
                    sExePath + @"/Assets/Playa-de-Formentor_Mallorca.jpg",
                    sExePath + @"/Assets/Trees_winter.jpg",
                    sExePath + @"/Assets/Trees_winter2.jpg",
                    sExePath + @"/Assets/Winter_sunset.jpg",
                    sExePath + @"/Assets/Yoho_National_Park_Winter_Night.jpg"};              
                foreach (string s in strings)
                {
                    ID2D1Bitmap pD2D1Bitmap = null;
                    IWICBitmapSource pWICBitmapSourceTmp = null;
                    hr = LoadBitmapFromFile(m_pD2DDeviceContext3, m_pWICImagingFactory, s,
                      0, 0, out pD2D1Bitmap, out pWICBitmapSourceTmp);
                    SafeRelease(ref pWICBitmapSourceTmp);
                    listImages.Add(pD2D1Bitmap);                   
                }
            }
            return hr;
        }

        //

        void GetWICBitmapSourceFromD2D1Bitmap(ID2D1Bitmap pD2D1Bitmap, ID2D1DeviceContext pD2D1DeviceContext, out IWICBitmapSource pWICBitmapSource)
        {
            pWICBitmapSource = null;
            IWICBitmapEncoder pEncoder = null;
            HRESULT hr = m_pWICImagingFactory2.CreateEncoder(GUID_ContainerFormatBmp, Guid.Empty, out pEncoder);
            if (hr == HRESULT.S_OK)
            {
                System.Runtime.InteropServices.ComTypes.IStream pStream = SHCreateMemStream(IntPtr.Zero, 0);
                hr = pEncoder.Initialize(pStream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
                if (hr == HRESULT.S_OK)
                {
                    IWICBitmapFrameEncode pFrameEncoder = null;
                    hr = pEncoder.CreateNewFrame(out pFrameEncoder, null);
                    if (hr == HRESULT.S_OK)
                    {
                        hr = pFrameEncoder.Initialize(null);
                        ID2D1Device pD2D1Device = null;
                        pD2D1DeviceContext.GetDevice(out pD2D1Device);
                        IWICImageEncoder pImageEncoder = null;
                        hr = m_pWICImagingFactory2.CreateImageEncoder(pD2D1Device, out pImageEncoder);
                        if (hr == HRESULT.S_OK)
                        {
                            hr = pImageEncoder.WriteFrame(pD2D1Bitmap, pFrameEncoder, IntPtr.Zero);
                            hr = pFrameEncoder.Commit();
                            hr = pEncoder.Commit();
                            pStream.Commit((int)STGC.STGC_DEFAULT);

                            System.Runtime.InteropServices.ComTypes.STATSTG stat;
                            pStream.Stat(out stat, 0);
                            byte[] pBytes = new byte[stat.cbSize];
                            IntPtr pRead = Marshal.AllocHGlobal(sizeof(int));
                            IntPtr newPos = IntPtr.Zero;
                            pStream.Seek(0, 0, newPos);
                            pStream.Read(pBytes, (int)stat.cbSize, pRead);
                            Marshal.FreeHGlobal(pRead);

                            // IWICFormatConverter IWICBitmapScaler IWICBitmap IWICBitmapFrameDecode

                            int nDataSize = pBytes.Length * Marshal.SizeOf(typeof(byte));
                            //int nDataSize = pBytes.Length * Marshal.SizeOf(typeof(uint));
                            IntPtr pData = Marshal.AllocHGlobal(nDataSize);
                            Marshal.Copy(pBytes, 0, pData, nDataSize);
                            m_pD2DBitmap.GetSize(out D2D1_SIZE_F bmpSize);
                            IWICBitmap pWICBitmap;

                            int nStride = (int)(bmpSize.width * 4);

                            Guid pixelFormat;
                            m_pD2DBitmap.GetPixelFormat(out D2D1_PIXEL_FORMAT D2DpixelFormat);
                            if (D2DpixelFormat.alphaMode == D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED)
                                pixelFormat = GUID_WICPixelFormat32bppBGRA;
                            else
                                pixelFormat = GUID_WICPixelFormat32bppBGR;

                            //var pixelFormat = (m_pD2DBitmap.GetPixelFormat().alphaMode == D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED)?GUID_WICPixelFormat32bppPBGRA:GUID_WICPixelFormat32bppBGR;

                            //int nSize = colorDepth == 3 ? (((stride + 3) / 4) * 4) * height : stride * height;
                            //float buffSize = ((((int)bmpSize.width * 4) + 3) & ~3) * bmpSize.height;
                            // hr = m_pWICImagingFactory2.CreateBitmapFromMemory((uint)bmpSize.width, (uint)bmpSize.height, ref pixelFormat,
                            //(uint)nStride, (uint)buffSize, pData, out pWICBitmap);

                            hr = m_pWICImagingFactory2.CreateBitmapFromMemory((uint)bmpSize.width, (uint)bmpSize.height, ref pixelFormat,
                                (uint)nStride, (uint)(nStride * bmpSize.height), pData, out pWICBitmap);

                            if (hr == HRESULT.S_OK)
                            {
                                IWICBitmapFlipRotator pWICBitmapFlipRotator = null;
                                hr = m_pWICImagingFactory2.CreateBitmapFlipRotator(out pWICBitmapFlipRotator);
                                if (hr == HRESULT.S_OK)
                                {
                                    hr = pWICBitmapFlipRotator.Initialize(pWICBitmap, WICBitmapTransformOptions.WICBitmapTransformFlipVertical);
                                    if (hr == HRESULT.S_OK)
                                    {
                                        pWICBitmapSource = pWICBitmapFlipRotator;
                                    }
                                    //SafeRelease(ref pWICBitmapFlipRotator);
                                }
                            }
                            Marshal.FreeHGlobal(pData);
                            SafeRelease(ref pImageEncoder);
                        }
                        SafeRelease(ref pD2D1Device);
                        SafeRelease(ref pFrameEncoder);
                    }
                    SafeRelease(ref pStream);
                }
                SafeRelease(ref pEncoder);
            }
        }

        void SaveD2D1BitmapToFile(ID2D1Bitmap pD2D1Bitmap, ID2D1DeviceContext pD2D1DeviceContext, string sFile)
        {
            IWICBitmapEncoder pEncoder = null;
            string sExtension = Path.GetExtension(sFile);
            Guid guidCodec = Guid.Empty;
            switch (sExtension)
            {
                case ".jpg":
                    guidCodec = GUID_ContainerFormatJpeg;
                    break;
                case ".png":
                    guidCodec = GUID_ContainerFormatPng;
                    break;
                case ".gif":
                    guidCodec = GUID_ContainerFormatGif;
                    break;
                case ".bmp":
                    guidCodec = GUID_ContainerFormatBmp;
                    break;
                case ".tif":
                    guidCodec = GUID_ContainerFormatTiff;
                    break;
            }
            //HRESULT hr = m_pWICImagingFactory2.CreateEncoder(GUID_ContainerFormatJpeg, Guid.Empty, out pEncoder);
            HRESULT hr = m_pWICImagingFactory2.CreateEncoder(guidCodec, Guid.Empty, out pEncoder);
            if (hr == HRESULT.S_OK)
            {
                IWICStream pStream = null;
                hr = m_pWICImagingFactory2.CreateStream(out pStream);
                if (hr == HRESULT.S_OK)
                {
                    hr = pStream.InitializeFromFilename(sFile, (int)GENERIC_WRITE);
                    if (hr == HRESULT.S_OK)
                    {
                        hr = pEncoder.Initialize(pStream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
                        if (hr == HRESULT.S_OK)
                        {
                            IWICBitmapFrameEncode pFrameEncoder = null;
                            hr = pEncoder.CreateNewFrame(out pFrameEncoder, null);
                            if (hr == HRESULT.S_OK)
                            {
                                hr = pFrameEncoder.Initialize(null);
                                //hr = spFrameEncoder->SetSize(nWidth, nHeight);

                                ID2D1Device pD2D1Device = null;
                                pD2D1DeviceContext.GetDevice(out pD2D1Device);
                                IWICImageEncoder pImageEncoder = null;
                                hr = m_pWICImagingFactory2.CreateImageEncoder(pD2D1Device, out pImageEncoder);
                                if (hr == HRESULT.S_OK)
                                {
                                    hr = pImageEncoder.WriteFrame(pD2D1Bitmap, pFrameEncoder, IntPtr.Zero);
                                    hr = pFrameEncoder.Commit();
                                    hr = pEncoder.Commit();
                                    pStream.Commit((int)STGC.STGC_DEFAULT);
                                    SafeRelease(ref pImageEncoder);
                                }
                                SafeRelease(ref pD2D1Device);
                                SafeRelease(ref pFrameEncoder);
                            }
                        }
                    }
                    SafeRelease(ref pStream);
                }
                SafeRelease(ref pEncoder);
            }
        }

        async void ConvertD2D1BitmapToBitmapImage(ID2D1Bitmap pD2D1Bitmap, ID2D1DeviceContext pD2D1DeviceContext, BitmapImage bi)
        {
            IWICBitmapEncoder pEncoder = null;
            HRESULT hr = m_pWICImagingFactory2.CreateEncoder(GUID_ContainerFormatJpeg, Guid.Empty, out pEncoder);
            if (hr == HRESULT.S_OK)
            {
                System.Runtime.InteropServices.ComTypes.IStream pStream = SHCreateMemStream(IntPtr.Zero, 0);
                hr = pEncoder.Initialize(pStream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
                if (hr == HRESULT.S_OK)
                {
                    IWICBitmapFrameEncode pFrameEncoder = null;
                    hr = pEncoder.CreateNewFrame(out pFrameEncoder, null);
                    if (hr == HRESULT.S_OK)
                    {
                        hr = pFrameEncoder.Initialize(null);
                        ID2D1Device pD2D1Device = null;
                        pD2D1DeviceContext.GetDevice(out pD2D1Device);
                        IWICImageEncoder pImageEncoder = null;
                        hr = m_pWICImagingFactory2.CreateImageEncoder(pD2D1Device, out pImageEncoder);
                        if (hr == HRESULT.S_OK)
                        {
                            hr = pImageEncoder.WriteFrame(pD2D1Bitmap, pFrameEncoder, IntPtr.Zero);
                            hr = pFrameEncoder.Commit();
                            hr = pEncoder.Commit();
                            pStream.Commit((int)STGC.STGC_DEFAULT);

                            System.Runtime.InteropServices.ComTypes.STATSTG stat;
                            pStream.Stat(out stat, 0);
                            byte[] pBytes = new byte[stat.cbSize];
                            IntPtr pRead = Marshal.AllocHGlobal(sizeof(int));
                            IntPtr newPos = IntPtr.Zero;
                            pStream.Seek(0, 0, newPos);
                            pStream.Read(pBytes, (int)stat.cbSize, pRead);
                            Marshal.FreeHGlobal(pRead);

                            //BitmapImage bi = new BitmapImage();
                            using (Windows.Storage.Streams.InMemoryRandomAccessStream inMemoryStream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                            {
                                await inMemoryStream.WriteAsync(pBytes.AsBuffer());
                                inMemoryStream.Seek(0);
                                //await bi.SetSourceAsync(inMemoryStream);
                                bi.SetSource(inMemoryStream);
                            }

                            //IRandomAccessStream randomAccessStream = pStream.AsRandomAccessStream();
                            //pStream.Seek(0);
                            //var image = new BitmapImage();
                            //await image.SetSourceAsync(pStream);
                            // Inverse CreateStreamOverRandomAccessStream 

                            SafeRelease(ref pImageEncoder);
                        }
                        SafeRelease(ref pD2D1Device);
                        SafeRelease(ref pFrameEncoder);
                    }
                    SafeRelease(ref pStream);
                }
                SafeRelease(ref pEncoder);
            }
        }

        // From : https://github.com/microsoft/Windows-classic-samples/blob/master/Samples/Win7Samples/multimedia/Direct2D/SimpleDirect2DApplication/SimpleDirect2dApplication.cpp

        HRESULT LoadBitmapFromFile(ID2D1DeviceContext3 pDeviceContext3, IWICImagingFactory pIWICFactory, string uri, uint destinationWidth,
            uint destinationHeight, out ID2D1Bitmap pD2DBitmap, out IWICBitmapSource pBitmapSource)
        {
            HRESULT hr = HRESULT.S_OK;
            pD2DBitmap = null;
            pBitmapSource = null;

            IWICBitmapDecoder pDecoder = null;
            IWICBitmapFrameDecode pSource = null;
            IWICFormatConverter pConverter = null;
            IWICBitmapScaler pScaler = null;

            hr = pIWICFactory.CreateDecoderFromFilename(uri, Guid.Empty, unchecked((int)GENERIC_READ), WICDecodeOptions.WICDecodeMetadataCacheOnLoad, out pDecoder);
            if (hr == HRESULT.S_OK)
            {
                hr = pDecoder.GetFrame(0, out pSource);
                if (hr == HRESULT.S_OK)
                {
                    hr = pIWICFactory.CreateFormatConverter(out pConverter);
                    if (hr == HRESULT.S_OK)
                    {
                        if (destinationWidth != 0 || destinationHeight != 0)
                        {
                            uint originalWidth, originalHeight;
                            hr = pSource.GetSize(out originalWidth, out originalHeight);
                            if (hr == HRESULT.S_OK)
                            {
                                if (destinationWidth == 0)
                                {
                                    float scalar = (float)(destinationHeight) / (float)(originalHeight);
                                    destinationWidth = (uint)(scalar * (float)(originalWidth));
                                }
                                else if (destinationHeight == 0)
                                {
                                    float scalar = (float)(destinationWidth) / (float)(originalWidth);
                                    destinationHeight = (uint)(scalar * (float)(originalHeight));
                                }
                                hr = pIWICFactory.CreateBitmapScaler(out pScaler);
                                if (hr == HRESULT.S_OK)
                                {
                                    hr = pScaler.Initialize(pSource, destinationWidth, destinationHeight, WICBitmapInterpolationMode.WICBitmapInterpolationModeCubic);
                                    if (hr == HRESULT.S_OK)
                                    {
                                        hr = pConverter.Initialize(pScaler, GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0.0f, WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                                        //hr = pConverter.Initialize(pScaler, GUID_WICPixelFormat32bppBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0.0f, WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                                    }
                                    Marshal.ReleaseComObject(pScaler);
                                }
                            }
                        }
                        else // Don't scale the image.
                        {
                            hr = pConverter.Initialize(pSource, GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0.0f, WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                            //hr = pConverter.Initialize(pSource, GUID_WICPixelFormat32bppBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0.0f, WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                        }

                        // Create a Direct2D bitmap from the WIC bitmap.
                        D2D1_BITMAP_PROPERTIES bitmapProperties = new D2D1_BITMAP_PROPERTIES();
                        bitmapProperties.pixelFormat = D2DTools.PixelFormat(DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED);
                        bitmapProperties.dpiX = 96;
                        bitmapProperties.dpiY = 96;
                        hr = pDeviceContext3.CreateBitmapFromWicBitmap(pConverter, bitmapProperties, out pD2DBitmap);

                        //if (pBitmapSource != null)
                        pBitmapSource = pConverter;
                    }
                    Marshal.ReleaseComObject(pSource);
                }
                Marshal.ReleaseComObject(pDecoder);
            }
            return hr;
        }


        private void cmbEffects_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CollapseEffectStackPanels();

            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();

            BitmapImage bitmapImage = new BitmapImage();
            if (sItem == " Composite")
            {
                //img.Width = bitmapImage.DecodePixelWidth = 80; //natural px width of image source
                // don't need to set Height, system maintains aspect ratio, and calculates the other
                // dimension, so long as one dimension measurement is provided
                bitmapImage.UriSource = new Uri(imgOrig.BaseUri, "Assets/Butterfly_Brown_745.png");
            }
            else if (sItem == " Shadow")
            {
                bitmapImage.UriSource = new Uri(imgOrig.BaseUri, "Assets/Flying_Parrot.png");
            }
            else
            {
                bitmapImage.UriSource = new Uri(imgOrig.BaseUri, "Assets/Cat_Flowers.jpg");
            }
            imgOrig.Source = bitmapImage;

            if (sItem == " Gaussian Blur")
            {
                spEffectGaussianBlur.Visibility = Visibility.Visible;
                EffectGaussianBlur();
            }
            else if (sItem == " Gamma Transfer")
            {
                spEffectGammaTransfer.Visibility = Visibility.Visible;
                EffectGammaTransfer();
            }
            else if (sItem == " Convolve Matrix")
            {
                spEffectConvolveMatrix.Visibility = Visibility.Visible;
                EffectConvolveMatrix();
            }
            else if (sItem == " Directional Blur")
            {
                spEffectDirectionalBlur.Visibility = Visibility.Visible;
                EffectDirectionalBlur();
            }
            else if (sItem == " Edge Detection")
            {
                spEffectEdgeDetection.Visibility = Visibility.Visible;
                EffectEdgeDetection();
            }
            else if (sItem == " Morphology")
            {
                spEffectMorphology.Visibility = Visibility.Visible;
                EffectMorphology();
            }
            else if (sItem == " Color Matrix")
            {
                spEffectColorMatrix.Visibility = Visibility.Visible;
                EffectColorMatrix();
            }
            else if (sItem == " Discrete Transfer")
            {
                spEffectDiscreteTransfer.Visibility = Visibility.Visible;
                EffectDiscreteTransfer();
            }
            else if (sItem == " Hue-to-RGB")
            {
                spEffectHueToRGB.Visibility = Visibility.Visible;
                EffectHueToRGB();
            }
            else if (sItem == " Hue Rotation")
            {
                spEffectHueRotation.Visibility = Visibility.Visible;
                EffectHueRotation();
            }
            else if (sItem == " Linear Transfer")
            {
                spEffectLinearTransfer.Visibility = Visibility.Visible;
                EffectLinearTransfer();
            }
            else if (sItem == " RGB-to-Hue")
            {
                spEffectRGBToHue.Visibility = Visibility.Visible;
                EffectRGBToHue();
            }
            else if (sItem == " Saturation")
            {
                spEffectSaturation.Visibility = Visibility.Visible;
                EffectSaturation();
            }
            else if (sItem == " Table Transfer")
            {
                spEffectTableTransfer.Visibility = Visibility.Visible;
                EffectTableTransfer();
            }
            else if (sItem == " Tint")
            {
                spEffectTint.Visibility = Visibility.Visible;
                EffectTint();
            }
            else if (sItem == " Displacement Map")
            {
                spEffectDisplacementMap.Visibility = Visibility.Visible;
                EffectDisplacementMap();
            }

            else if (sItem == " Distant-Diffuse lighting")
            {
                spEffectDistantDiffuseLighting.Visibility = Visibility.Visible;
                EffectDistantDiffuseLighting();
            }
            else if (sItem == " Distant-Specular lighting")
            {
                spEffectDistantSpecularLighting.Visibility = Visibility.Visible;
                EffectDistantSpecularLighting();
            }
            else if (sItem == " Emboss")
            {
                spEffectEmboss.Visibility = Visibility.Visible;
                EffectEmboss();
            }
            else if (sItem == " Point-Diffuse lighting")
            {
                spEffectPointDiffuseLighting.Visibility = Visibility.Visible;
                EffectPointDiffuseLighting();
            }
            else if (sItem == " Point-Specular lighting")
            {
                spEffectPointSpecularLighting.Visibility = Visibility.Visible;
                EffectPointSpecularLighting();
            }
            else if (sItem == " Posterize")
            {
                spEffectPosterize.Visibility = Visibility.Visible;
                EffectPosterize();
            }
            else if (sItem == " Shadow")
            {
                spEffectShadow.Visibility = Visibility.Visible;
                EffectShadow();
            }
            else if (sItem == " Spot-Diffuse lighting")
            {
                spEffectSpotDiffuseLighting.Visibility = Visibility.Visible;
                EffectSpotDiffuseLighting();
            }
            else if (sItem == " Spot-Specular lighting")
            {
                spEffectSpotSpecularLighting.Visibility = Visibility.Visible;
                EffectSpotSpecularLighting();
            }
            else if (sItem == " Turbulence")
            {
                spEffectTurbulence.Visibility = Visibility.Visible;
                EffectTurbulence();
            }
            else if (sItem == " Brightness")
            {
                spEffectBrightness.Visibility = Visibility.Visible;
                EffectBrightness();
            }
            else if (sItem == " Contrast")
            {
                spEffectContrast.Visibility = Visibility.Visible;
                EffectContrast();
            }
            else if (sItem == " Exposure")
            {
                spEffectExposure.Visibility = Visibility.Visible;
                EffectExposure();
            }
            else if (sItem == " Grayscale")
            {
                spEffectGrayscale.Visibility = Visibility.Visible;
                EffectGrayscale();
            }
            else if (sItem == " Highlights and Shadows")
            {
                spEffectHighlightsAndShadows.Visibility = Visibility.Visible;
                EffectHighlightsAndShadows();
            }
            else if (sItem == " Invert")
            {
                spEffectInvert.Visibility = Visibility.Visible;
                EffectInvert();
            }
            else if (sItem == " Sepia")
            {
                spEffectSepia.Visibility = Visibility.Visible;
                EffectSepia();
            }
            else if (sItem == " Sharpen")
            {
                spEffectSharpen.Visibility = Visibility.Visible;
                EffectSharpen();
            }
            else if (sItem == " Straighten")
            {
                spEffectStraighten.Visibility = Visibility.Visible;
                EffectStraighten();
            }
            else if (sItem == " Temperature and Tint")
            {
                spEffectTemperatureAndTint.Visibility = Visibility.Visible;
                EffectTemperatureAndTint();
            }
            else if (sItem == " Vignette")
            {
                spEffectVignette.Visibility = Visibility.Visible;
                EffectVignette();
            }
            else if (sItem == " 2D Affine Transform")
            {
                spEffectAffineTransform.Visibility = Visibility.Visible;
                EffectAffineTransform();
            }
            else if (sItem == " 3D Transform")
            {
                spEffectTransform.Visibility = Visibility.Visible;
                EffectTransform();
            }
            else if (sItem == " Perspective Transform")
            {
                spEffectPerspectiveTransform.Visibility = Visibility.Visible;
                EffectPerspectiveTransform();
            }
            else if (sItem == " Atlas")
            {
                spEffectAtlas.Visibility = Visibility.Visible;
                EffectAtlas();
            }
            else if (sItem == " Border")
            {
                spEffectBorder.Visibility = Visibility.Visible;
                EffectBorder();
            }
            else if (sItem == " Crop")
            {
                spEffectCrop.Visibility = Visibility.Visible;
                EffectCrop();
            }
            else if (sItem == " Scale")
            {
                spEffectScale.Visibility = Visibility.Visible;
                EffectScale();
            }
            else if (sItem == " Tile")
            {
                spEffectTile.Visibility = Visibility.Visible;
                EffectTile();
            }
            else if (sItem == " Chroma-Key")
            {
                spEffectChromaKey.Visibility = Visibility.Visible;
                EffectChromaKey();
            }
            else if (sItem == " Luminance To Alpha")
            {
                spEffectLuminanceToAlpha.Visibility = Visibility.Visible;
                EffectLuminanceToAlpha();
            }
            else if (sItem == " Opacity")
            {
                spEffectOpacity.Visibility = Visibility.Visible;
                EffectOpacity();
            }
            else if (sItem == " Alpha Mask")
            {
                spEffectAlphaMask.Visibility = Visibility.Visible;
                EffectAlphaMask();
            }
            else if (sItem == " Arithmetic Composite")
            {
                spEffectArithmeticComposite.Visibility = Visibility.Visible;
                EffectArithmeticComposite();
            }
            else if (sItem == " Blend")
            {
                spEffectBlend.Visibility = Visibility.Visible;
                EffectBlend();
            }
            else if (sItem == " Composite")
            {
                spEffectComposite.Visibility = Visibility.Visible;
                EffectComposite();
            }
            else if (sItem == " Cross-Fade")
            {
                spEffectCrossFade.Visibility = Visibility.Visible;
                EffectCrossFade();
            }
        }

        private void cmbAnimations_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { 
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Translate")
            {
                _Animation = (uint)ANIMATION.ANIMATION_TRANSLATE;
            }
            else if (sItem == "Cross-Fade")
            {
                _Animation = (uint)ANIMATION.ANIMATION_CROSSFADE;
            }
            else if (sItem == "Perspective")
            {
                _Animation = (uint)ANIMATION.ANIMATION_PERSPECTIVE;
            }
            else if (sItem == "Blur")
            {
                _Animation = (uint)ANIMATION.ANIMATION_BLUR;
            }
            else if (sItem == "Crop")
            {
                _Animation = (uint)ANIMATION.ANIMATION_CROP;
            }
            else if (sItem == "Brightness")
            {
                _Animation = (uint)ANIMATION.ANIMATION_BRIGHTNESS;
            }
            else if (sItem == "Rotate")
            {
                _Animation = (uint)ANIMATION.ANIMATION_ROTATE;
            }
            else if (sItem == "Grid Mask")
            {
                _Animation = (uint)ANIMATION.ANIMATION_GRIDMASK;
            }
            else if (sItem == "Chroma-Key")
            {
                _Animation = (uint)ANIMATION.ANIMATION_CHROMA_KEY;
            }
            else if (sItem == "Morphology")
            {
                _Animation = (uint)ANIMATION.ANIMATION_MORPHOLOGY;
            }
            else if (sItem == "Zoom")
            {
                _Animation = (uint)ANIMATION.ANIMATION_ZOOM;
            }
            else if (sItem == "Displacement/Turbulence")
            {
                _Animation = (uint)ANIMATION.ANIMATION_TURBULENCE;
            }
        }


        private void cmbOptimizationGaussianBlur_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Speed")
            {
                _OptimizationGaussianBlur = (uint)D2D1_GAUSSIANBLUR_OPTIMIZATION.D2D1_GAUSSIANBLUR_OPTIMIZATION_SPEED;
            }
            else if (sItem == "Balanced")
            {
                _OptimizationGaussianBlur = (uint)D2D1_GAUSSIANBLUR_OPTIMIZATION.D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED;
            }
            else if (sItem == "Quality")
            {
                _OptimizationGaussianBlur = (uint)D2D1_GAUSSIANBLUR_OPTIMIZATION.D2D1_GAUSSIANBLUR_OPTIMIZATION_QUALITY;
            }

            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Gaussian Blur")
                EffectGaussianBlur();
        }

        private void cmbOptimizationShadow_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Speed")
            {
                _OptimizationShadow = (uint)D2D1_SHADOW_OPTIMIZATION.D2D1_SHADOW_OPTIMIZATION_SPEED;
            }
            else if (sItem == "Balanced")
            {
                _OptimizationShadow = (uint)D2D1_SHADOW_OPTIMIZATION.D2D1_SHADOW_OPTIMIZATION_BALANCED;
            }
            else if (sItem == "Quality")
            {
                _OptimizationShadow = (uint)D2D1_SHADOW_OPTIMIZATION.D2D1_SHADOW_OPTIMIZATION_QUALITY;
            }

            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Shadow")
                EffectShadow();
        }

        private void cmbBorderModeGaussianBlur_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Soft")
            {
                _BorderModeGaussianBlur = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
            }
            else if (sItem == "Hard")
            {
                _BorderModeGaussianBlur = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD;
            }
            if (m_pD2DBitmap != null)
                EffectGaussianBlur();
        }

        private void cmbOptimizationDirectionalBlur_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Speed")
            {
                _OptimizationDirectionalBlur = (uint)D2D1_DIRECTIONALBLUR_OPTIMIZATION.D2D1_DIRECTIONALBLUR_OPTIMIZATION_SPEED;
            }
            else if (sItem == "Balanced")
            {
                _OptimizationDirectionalBlur = (uint)D2D1_DIRECTIONALBLUR_OPTIMIZATION.D2D1_DIRECTIONALBLUR_OPTIMIZATION_BALANCED;
            }
            else if (sItem == "Quality")
            {
                _OptimizationDirectionalBlur = (uint)D2D1_DIRECTIONALBLUR_OPTIMIZATION.D2D1_DIRECTIONALBLUR_OPTIMIZATION_QUALITY;
            }

            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Directional Blur")
                EffectDirectionalBlur();
        }

        private void cmbBorderModeDirectionalBlur_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Soft")
            {
                _BorderModeDirectionalBlur = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
            }
            else if (sItem == "Hard")
            {
                _BorderModeDirectionalBlur = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD;
            }
            if (m_pD2DBitmap != null)
                EffectDirectionalBlur();
        }

        private void cmbModeEdgeDetection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Sobel")
            {
                _ModeEdgeDetection = (uint)D2D1_EDGEDETECTION_MODE.D2D1_EDGEDETECTION_MODE_SOBEL;
            }
            else if (sItem == "Prewitt")
            {
                _ModeEdgeDetection = (uint)D2D1_EDGEDETECTION_MODE.D2D1_EDGEDETECTION_MODE_PREWITT;
            }
            if (m_pD2DBitmap != null)
                EffectEdgeDetection();
        }

        private void cmbModeMorphology_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Erode")
            {
                _ModeMorphology = (uint)D2D1_MORPHOLOGY_MODE.D2D1_MORPHOLOGY_MODE_ERODE;
            }
            else if (sItem == "Dilate")
            {
                _ModeMorphology = (uint)D2D1_MORPHOLOGY_MODE.D2D1_MORPHOLOGY_MODE_DILATE;
            }
            if (m_pD2DBitmap != null)
                EffectMorphology();
        }
 
        private void cmbBorderModeMatrix_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Soft")
            {
                _BorderModeMatrix = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
            }
            else if (sItem == "Hard")
            {
                _BorderModeMatrix = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD;
            }
            if (m_pD2DBitmap != null)
                EffectConvolveMatrix();
        }

        private void cmbHueToRGB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Hue Saturation Value (HSV) to RGB")
            {
                _InputColorSpaceHueToRGB = (uint)D2D1_HUETORGB_INPUT_COLOR_SPACE.D2D1_HUETORGB_INPUT_COLOR_SPACE_HUE_SATURATION_VALUE;
            }
            else if (sItem == "Hue Saturation Lightness (HSL) to RGB")
            {
                _InputColorSpaceHueToRGB = (uint)D2D1_HUETORGB_INPUT_COLOR_SPACE.D2D1_HUETORGB_INPUT_COLOR_SPACE_HUE_SATURATION_LIGHTNESS;
            }
            if (m_pD2DBitmap != null)
                EffectHueToRGB();
        }

        private void cmbRGBToHue_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "RGB to Hue Saturation Value (HSV)")
            {
                _OutputColorSpaceRGBToHue = (uint)D2D1_RGBTOHUE_OUTPUT_COLOR_SPACE.D2D1_RGBTOHUE_OUTPUT_COLOR_SPACE_HUE_SATURATION_VALUE;
            }
            else if (sItem == "RGB to Hue Saturation Lightness (HSL)")
            {
                _OutputColorSpaceRGBToHue = (uint)D2D1_RGBTOHUE_OUTPUT_COLOR_SPACE.D2D1_RGBTOHUE_OUTPUT_COLOR_SPACE_HUE_SATURATION_LIGHTNESS;
            }
            if (m_pD2DBitmap != null)
                EffectRGBToHue();
        }

        private void cmbScaleModeMatrix_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _ScaleModeMatrix = (uint)D2D1_CONVOLVEMATRIX_SCALE_MODE.D2D1_CONVOLVEMATRIX_SCALE_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _ScaleModeMatrix = (uint)D2D1_CONVOLVEMATRIX_SCALE_MODE.D2D1_CONVOLVEMATRIX_SCALE_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _ScaleModeMatrix = (uint)D2D1_CONVOLVEMATRIX_SCALE_MODE.D2D1_CONVOLVEMATRIX_SCALE_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _ScaleModeMatrix = (uint)D2D1_CONVOLVEMATRIX_SCALE_MODE.D2D1_CONVOLVEMATRIX_SCALE_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _ScaleModeMatrix = (uint)D2D1_CONVOLVEMATRIX_SCALE_MODE.D2D1_CONVOLVEMATRIX_SCALE_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _ScaleModeMatrix = (uint)D2D1_CONVOLVEMATRIX_SCALE_MODE.D2D1_CONVOLVEMATRIX_SCALE_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Convolve Matrix")
                EffectConvolveMatrix();
        }

        private void cmbScaleModeDistantDiffuse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _ScaleModeDistantDiffuse = (uint)D2D1_DISTANTDIFFUSE_SCALE_MODE.D2D1_DISTANTDIFFUSE_SCALE_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _ScaleModeDistantDiffuse = (uint)D2D1_DISTANTDIFFUSE_SCALE_MODE.D2D1_DISTANTDIFFUSE_SCALE_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _ScaleModeDistantDiffuse = (uint)D2D1_DISTANTDIFFUSE_SCALE_MODE.D2D1_DISTANTDIFFUSE_SCALE_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _ScaleModeDistantDiffuse = (uint)D2D1_DISTANTDIFFUSE_SCALE_MODE.D2D1_DISTANTDIFFUSE_SCALE_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _ScaleModeDistantDiffuse = (uint)D2D1_DISTANTDIFFUSE_SCALE_MODE.D2D1_DISTANTDIFFUSE_SCALE_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _ScaleModeDistantDiffuse = (uint)D2D1_DISTANTDIFFUSE_SCALE_MODE.D2D1_DISTANTDIFFUSE_SCALE_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Distant-diffuse lighting")
                EffectDistantDiffuseLighting();
        }

        private void cmbScaleModePointDiffuse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _ScaleModePointDiffuse = (uint)D2D1_POINTDIFFUSE_SCALE_MODE.D2D1_POINTDIFFUSE_SCALE_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _ScaleModePointDiffuse = (uint)D2D1_POINTDIFFUSE_SCALE_MODE.D2D1_POINTDIFFUSE_SCALE_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _ScaleModePointDiffuse = (uint)D2D1_POINTDIFFUSE_SCALE_MODE.D2D1_POINTDIFFUSE_SCALE_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _ScaleModePointDiffuse = (uint)D2D1_POINTDIFFUSE_SCALE_MODE.D2D1_POINTDIFFUSE_SCALE_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _ScaleModePointDiffuse = (uint)D2D1_POINTDIFFUSE_SCALE_MODE.D2D1_POINTDIFFUSE_SCALE_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _ScaleModePointDiffuse = (uint)D2D1_POINTDIFFUSE_SCALE_MODE.D2D1_POINTDIFFUSE_SCALE_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Point-Diffuse lighting")
                EffectPointDiffuseLighting();
        }

        private void cmbScaleModeDistantSpecular_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _ScaleModeDistantSpecular = (uint)D2D1_DISTANTSPECULAR_SCALE_MODE.D2D1_DISTANTSPECULAR_SCALE_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _ScaleModeDistantSpecular = (uint)D2D1_DISTANTSPECULAR_SCALE_MODE.D2D1_DISTANTSPECULAR_SCALE_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _ScaleModeDistantSpecular = (uint)D2D1_DISTANTSPECULAR_SCALE_MODE.D2D1_DISTANTSPECULAR_SCALE_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _ScaleModeDistantDiffuse = (uint)D2D1_DISTANTSPECULAR_SCALE_MODE.D2D1_DISTANTSPECULAR_SCALE_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _ScaleModeDistantSpecular = (uint)D2D1_DISTANTSPECULAR_SCALE_MODE.D2D1_DISTANTSPECULAR_SCALE_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _ScaleModeDistantSpecular = (uint)D2D1_DISTANTSPECULAR_SCALE_MODE.D2D1_DISTANTSPECULAR_SCALE_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Distant-Specular lighting")
                EffectDistantSpecularLighting();
        }

        private void cmbScaleModePointSpecular_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _ScaleModePointSpecular = (uint)D2D1_POINTSPECULAR_SCALE_MODE.D2D1_POINTSPECULAR_SCALE_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _ScaleModePointSpecular = (uint)D2D1_POINTSPECULAR_SCALE_MODE.D2D1_POINTSPECULAR_SCALE_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _ScaleModePointSpecular = (uint)D2D1_POINTSPECULAR_SCALE_MODE.D2D1_POINTSPECULAR_SCALE_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _ScaleModePointSpecular = (uint)D2D1_POINTSPECULAR_SCALE_MODE.D2D1_POINTSPECULAR_SCALE_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _ScaleModePointSpecular = (uint)D2D1_POINTSPECULAR_SCALE_MODE.D2D1_POINTSPECULAR_SCALE_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _ScaleModePointSpecular = (uint)D2D1_POINTSPECULAR_SCALE_MODE.D2D1_POINTSPECULAR_SCALE_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Point-Specular lighting")
                EffectPointSpecularLighting();
        }

        private void cmbScaleModeSpotDiffuse_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _ScaleModeSpotDiffuse = (uint)D2D1_SPOTDIFFUSE_SCALE_MODE.D2D1_SPOTDIFFUSE_SCALE_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _ScaleModeSpotDiffuse = (uint)D2D1_SPOTDIFFUSE_SCALE_MODE.D2D1_SPOTDIFFUSE_SCALE_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _ScaleModeSpotDiffuse = (uint)D2D1_SPOTDIFFUSE_SCALE_MODE.D2D1_SPOTDIFFUSE_SCALE_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _ScaleModeSpotDiffuse = (uint)D2D1_SPOTDIFFUSE_SCALE_MODE.D2D1_SPOTDIFFUSE_SCALE_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _ScaleModeSpotDiffuse = (uint)D2D1_SPOTDIFFUSE_SCALE_MODE.D2D1_SPOTDIFFUSE_SCALE_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _ScaleModeSpotDiffuse = (uint)D2D1_SPOTDIFFUSE_SCALE_MODE.D2D1_SPOTDIFFUSE_SCALE_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Spot-Diffuse lighting")
                EffectSpotDiffuseLighting();
        }

        private void cmbScaleModeSpotSpecular_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _ScaleModeSpotSpecular = (uint)D2D1_SPOTSPECULAR_SCALE_MODE.D2D1_SPOTSPECULAR_SCALE_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _ScaleModeSpotSpecular = (uint)D2D1_SPOTSPECULAR_SCALE_MODE.D2D1_SPOTSPECULAR_SCALE_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _ScaleModeSpotSpecular = (uint)D2D1_SPOTSPECULAR_SCALE_MODE.D2D1_SPOTSPECULAR_SCALE_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _ScaleModeSpotSpecular = (uint)D2D1_SPOTSPECULAR_SCALE_MODE.D2D1_SPOTSPECULAR_SCALE_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _ScaleModeSpotDiffuse = (uint)D2D1_SPOTSPECULAR_SCALE_MODE.D2D1_SPOTSPECULAR_SCALE_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _ScaleModeSpotSpecular = (uint)D2D1_SPOTSPECULAR_SCALE_MODE.D2D1_SPOTSPECULAR_SCALE_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Spot-Specular lighting")
                EffectSpotSpecularLighting();
        }

        private void cmbInputGammaHighlightsAndShadows_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Linear")
            {
                _HighlightsAndShadowsMaskInputGamma = (uint)D2D1_HIGHLIGHTSANDSHADOWS_INPUT_GAMMA.D2D1_HIGHLIGHTSANDSHADOWS_INPUT_GAMMA_LINEAR;
            }
            else if (sItem == "sRGB")
            {
                _HighlightsAndShadowsMaskInputGamma = (uint)D2D1_HIGHLIGHTSANDSHADOWS_INPUT_GAMMA.D2D1_HIGHLIGHTSANDSHADOWS_INPUT_GAMMA_SRGB;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " HighlightsAndShadows")
                EffectHighlightsAndShadows();
        }

        private void cmbScaleModeStraighten_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _ScaleModeStraighten = (uint)D2D1_STRAIGHTEN_SCALE_MODE.D2D1_STRAIGHTEN_SCALE_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _ScaleModeStraighten = (uint)D2D1_STRAIGHTEN_SCALE_MODE.D2D1_STRAIGHTEN_SCALE_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _ScaleModeStraighten = (uint)D2D1_STRAIGHTEN_SCALE_MODE.D2D1_STRAIGHTEN_SCALE_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _ScaleModeStraighten = (uint)D2D1_STRAIGHTEN_SCALE_MODE.D2D1_STRAIGHTEN_SCALE_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _ScaleModeStraighten = (uint)D2D1_STRAIGHTEN_SCALE_MODE.D2D1_STRAIGHTEN_SCALE_MODE_ANISOTROPIC;
            }
            //else if (sItem == "High Quality Cubic")
            //{
            //    _ScaleModeStraighten = (uint)D2D1_STRAIGHTEN_SCALE_MODE.D2D1_STRAIGHTEN_SCALE_MODE_HIGH_QUALITY_CUBIC;
            //}
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Straighten")
                EffectStraighten();
        }

        private void cmbInterpolationModeAffineTransform_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _InterpolationModeAffineTransform = (uint)D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE.D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _InterpolationModeAffineTransform = (uint)D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE.D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _InterpolationModeAffineTransform = (uint)D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE.D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _InterpolationModeAffineTransform = (uint)D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE.D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _InterpolationModeAffineTransform = (uint)D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE.D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _InterpolationModeAffineTransform = (uint)D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE.D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " 2D Affine Transform")
                EffectAffineTransform();
        }

        private void cmbBorderModeAffineTransform_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Soft")
            {
                _BorderModeAffineTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
            }
            else if (sItem == "Hard")
            {
                _BorderModeAffineTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD;
            }
            if (m_pD2DBitmap != null)
                EffectAffineTransform();
        }

        private void cmbInterpolationModeTransform_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _InterpolationModeTransform = (uint)D2D1_3DTRANSFORM_INTERPOLATION_MODE.D2D1_3DTRANSFORM_INTERPOLATION_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _InterpolationModeTransform = (uint)D2D1_3DTRANSFORM_INTERPOLATION_MODE.D2D1_3DTRANSFORM_INTERPOLATION_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _InterpolationModeTransform = (uint)D2D1_3DTRANSFORM_INTERPOLATION_MODE.D2D1_3DTRANSFORM_INTERPOLATION_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _InterpolationModeTransform = (uint)D2D1_3DTRANSFORM_INTERPOLATION_MODE.D2D1_3DTRANSFORM_INTERPOLATION_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _InterpolationModeTransform = (uint)D2D1_3DTRANSFORM_INTERPOLATION_MODE.D2D1_3DTRANSFORM_INTERPOLATION_MODE_ANISOTROPIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " 3D Transform")
                EffectTransform();
        }

        private void cmbBorderModeTransform_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Soft")
            {
                _BorderModeTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
            }
            else if (sItem == "Hard")
            {
                _BorderModeTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD;
            }
            if (m_pD2DBitmap != null)
                EffectTransform();
        }

        private void cmbInterpolationModePerspectiveTransform_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _InterpolationModePerspectiveTransform = (uint)D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE.D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _InterpolationModePerspectiveTransform = (uint)D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE.D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _InterpolationModePerspectiveTransform = (uint)D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE.D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _InterpolationModePerspectiveTransform = (uint)D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE.D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _InterpolationModePerspectiveTransform = (uint)D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE.D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE_ANISOTROPIC;
            }
            //else if (sItem == "High Quality Cubic")
            //{
            //    _InterpolationModePerspectiveTransform = (uint)D2D1_3DPERSPECTIVETRANSFORM_INTERPOLATION_MODE.D2D1_SPOTSPECULAR_SCALE_MODE_HIGH_QUALITY_CUBIC;
            //}
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Perspective Transform")
                EffectPerspectiveTransform();
        }

        private void cmbBorderModePerspectiveTransform_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Soft")
            {
                _BorderModePerspectiveTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
            }
            else if (sItem == "Hard")
            {
                _BorderModePerspectiveTransform = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD;
            }
            if (m_pD2DBitmap != null)
                EffectPerspectiveTransform();
        }

        private void cmbBorderEdgeModeX_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Clamp")
            {
                _BorderEdgeModeX = (int)D2D1_BORDER_EDGE_MODE.D2D1_BORDER_EDGE_MODE_CLAMP;
            }
            else if (sItem == "Wrap")
            {
                _BorderEdgeModeX = (int)D2D1_BORDER_EDGE_MODE.D2D1_BORDER_EDGE_MODE_WRAP;
            }
            else if (sItem == "Mirror")
            {
                _BorderEdgeModeX = (int)D2D1_BORDER_EDGE_MODE.D2D1_BORDER_EDGE_MODE_MIRROR;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Border")
                EffectBorder();
        }

        private void cmbBorderEdgeModeY_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Clamp")
            {
                _BorderEdgeModeY = (int)D2D1_BORDER_EDGE_MODE.D2D1_BORDER_EDGE_MODE_CLAMP;
            }
            else if (sItem == "Wrap")
            {
                _BorderEdgeModeY = (int)D2D1_BORDER_EDGE_MODE.D2D1_BORDER_EDGE_MODE_WRAP;
            }
            else if (sItem == "Mirror")
            {
                _BorderEdgeModeY = (int)D2D1_BORDER_EDGE_MODE.D2D1_BORDER_EDGE_MODE_MIRROR;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Border")
                EffectBorder();
        }

        private void cmbBorderModeCrop_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Soft")
            {
                _BorderModeCrop = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
            }
            else if (sItem == "Hard")
            {
                _BorderModeCrop = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD;
            }
            if (m_pD2DBitmap != null)
                EffectCrop();
        }

        private void cmbBorderModeScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Soft")
            {
                _BorderModeScale = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_SOFT;
            }
            else if (sItem == "Hard")
            {
                _BorderModeScale = (uint)D2D1_BORDER_MODE.D2D1_BORDER_MODE_HARD;
            }
            if (m_pD2DBitmap != null)
                EffectScale();
        }

        private void cmbInterpolationModeScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Nearest Neighbor")
            {
                _InterpolationModeScale = (uint)D2D1_SCALE_INTERPOLATION_MODE.D2D1_SCALE_INTERPOLATION_MODE_NEAREST_NEIGHBOR;
            }
            else if (sItem == "Linear")
            {
                _InterpolationModeScale = (uint)D2D1_SCALE_INTERPOLATION_MODE.D2D1_SCALE_INTERPOLATION_MODE_LINEAR;
            }
            else if (sItem == "Cubic")
            {
                _InterpolationModeScale = (uint)D2D1_SCALE_INTERPOLATION_MODE.D2D1_SCALE_INTERPOLATION_MODE_CUBIC;
            }
            else if (sItem == "Multi Sample Linear")
            {
                _InterpolationModeScale = (uint)D2D1_SCALE_INTERPOLATION_MODE.D2D1_SCALE_INTERPOLATION_MODE_MULTI_SAMPLE_LINEAR;
            }
            else if (sItem == "Anisotropic")
            {
                _InterpolationModeScale = (uint)D2D1_SCALE_INTERPOLATION_MODE.D2D1_SCALE_INTERPOLATION_MODE_ANISOTROPIC;
            }
            else if (sItem == "High Quality Cubic")
            {
                _InterpolationModeScale = (uint)D2D1_SCALE_INTERPOLATION_MODE.D2D1_SCALE_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC;
            }
            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Scale")
                EffectScale();
        }

        private void cmbBlendModeBlend_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Darken")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_DARKEN;
            }
            else if (sItem == "Multiply")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_MULTIPLY;
            }
            else if (sItem == "Color Burn")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_COLOR_BURN;
            }
            else if (sItem == "Linear Burn")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_LINEAR_BURN;
            }
            else if (sItem == "Darker Color")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_DARKER_COLOR;
            }
            else if (sItem == "Lighten")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_LIGHTEN;
            }
            else if (sItem == "Screen")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_SCREEN;
            }
            else if (sItem == "Color Dodge")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_COLOR_DODGE;
            }
            else if (sItem == "Linear Dodge")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_LINEAR_DODGE;
            }
            else if (sItem == "Lighter Color")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_LIGHTER_COLOR;
            }
            else if (sItem == "Overlay")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_OVERLAY;
            }
            else if (sItem == "Soft Light")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_SOFT_LIGHT;
            }
            else if (sItem == "Hard Light")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_HARD_LIGHT;
            }
            else if (sItem == "Vivid Light")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_VIVID_LIGHT;
            }
            else if (sItem == "Linear Light")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_LINEAR_LIGHT;
            }
            else if (sItem == "Pin Light")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_PIN_LIGHT;
            }
            else if (sItem == "Hard Mix")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_HARD_MIX;
            }
            else if (sItem == "Difference")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_DIFFERENCE;
            }
            else if (sItem == "Exclusion")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_EXCLUSION;
            }
            else if (sItem == "Hue")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_HUE;
            }
            else if (sItem == "Saturation")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_SATURATION;
            }
            else if (sItem == "Color")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_COLOR;
            }
            else if (sItem == "Luminosity")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_LUMINOSITY;
            }
            else if (sItem == "Dissolve")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_DISSOLVE;
            }
            else if (sItem == "Subtract")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_SUBTRACT;
            }
            else if (sItem == "Division")
            {
                _BlendModeBlend = (uint)D2D1_BLEND_MODE.D2D1_BLEND_MODE_DIVISION;
            }

            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Blend")
                EffectBlend();
        }

        private void cmbCompositeModeComposite_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Source Over")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER;
            }
            else if (sItem == "Destination Over")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_DESTINATION_OVER;
            }
            else if (sItem == "Source In")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_IN;
            }
            else if (sItem == "Destination In")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_DESTINATION_IN;
            }
            else if (sItem == "Source Out")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OUT;
            }
            else if (sItem == "Destination Out")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_DESTINATION_OUT;
            }
            else if (sItem == "Source Atop")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_ATOP;
            }
            else if (sItem == "Destination Atop")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_DESTINATION_ATOP;
            }
            else if (sItem == "XOR")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_XOR;
            }
            else if (sItem == "Plus")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_PLUS;
            }
            else if (sItem == "Source Copy")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_COPY;
            }
            else if (sItem == "Bounded Source Copy")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_BOUNDED_SOURCE_COPY;
            }
            else if (sItem == "Mask Invert")
            {
                _CompositeModeComposite = (uint)D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_MASK_INVERT;
            }

            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (m_pD2DBitmap != null && sEffect == " Composite")
                EffectComposite();
        }

        private void cmbNoiseTurbulence_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ((ComboBox)sender).SelectedItem;
            var sItem = ((Microsoft.UI.Xaml.Controls.ContentControl)item).Content.ToString();
            if (sItem == "Fractal Sum")
            {
                _TurbulenceNoise = (uint)D2D1_TURBULENCE_NOISE.D2D1_TURBULENCE_NOISE_FRACTAL_SUM;
            }
            else if (sItem == "Turbulence")
            {
                _TurbulenceNoise = (uint)D2D1_TURBULENCE_NOISE.D2D1_TURBULENCE_NOISE_TURBULENCE;
            }
            if (m_pD2DBitmap != null)
                EffectTurbulence();
        }


        private void tsStraightenMaintainSize_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectStraighten();
        }

        private void tsRed_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectGammaTransfer();
        }

        private void tsGreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectGammaTransfer();
        }

        private void tsBlue_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectGammaTransfer();
        }

        private void tsAlpha_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectGammaTransfer();
        }

        private void tsOverlay_Edges_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectEdgeDetection();
        }

        private void tsDiscreteTransferRed_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectDiscreteTransfer();
        }

        private void tsDiscreteTransferGreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectDiscreteTransfer();
        }

        private void tsDiscreteTransferBlue_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectDiscreteTransfer();
        }

        private void tsDiscreteTransferAlpha_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectDiscreteTransfer();
        }

        private void tsTableTransferRed_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectTableTransfer();
        }

        private void tsTableTransferGreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectTableTransfer();
        }

        private void tsTableTransferBlue_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectTableTransfer();
        }

        private void tsTableTransferAlpha_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectTableTransfer();
        }

        private void tsClampOutput_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectGammaTransfer();
        }

        private void tsLinearTransferRed_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectLinearTransfer();
        }

        private void tsLinearTransferGreen_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectLinearTransfer();
        }

        private void tsLinearTransferBlue_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectLinearTransfer();
        }

        private void tsLinearTransferAlpha_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectLinearTransfer();
        }

        private void tsChromaKeyInvertAlpha_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectChromaKey();
        }

        private void tsChromaKeyFeather_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectChromaKey();
        }

        private void tsTurbulenceStitchable_Toggled(object sender, RoutedEventArgs e)
        {
            if (m_pD2DBitmap != null)
                EffectTurbulence();
        }

        private StackPanel _currentEffectStackPanel = null;
        private void tsEffectAnim_Toggled(object sender, RoutedEventArgs e)
        {
            ToggleSwitch ts = sender as ToggleSwitch;
            if (ts.IsOn)
            {
                if (_currentEffectStackPanel != null)
                    _currentEffectStackPanel.Visibility = Visibility.Visible;
                spEff.Visibility = Visibility.Visible;               
                borderImgOrig.Visibility = Visibility.Visible;
                borderImgEffect.Visibility = Visibility.Visible;
                spAnim.Visibility = Visibility.Collapsed;
                borderSCP.Visibility = Visibility.Collapsed;
                btnSave.Visibility = Visibility.Visible;
            }
            else
            {
                _currentEffectStackPanel = GetCurrentEffectStackPanel();
                CollapseEffectStackPanels();
                spAnim.Visibility = Visibility.Visible;
                borderSCP.Visibility = Visibility.Visible;
                spEff.Visibility = Visibility.Collapsed;
                borderImgOrig.Visibility = Visibility.Collapsed;
                borderImgEffect.Visibility = Visibility.Collapsed;
                btnSave.Visibility = Visibility.Collapsed;
            }
        }

        private void btnApplyConvolveMatrix_Click(object sender, RoutedEventArgs e)
        {
            EffectConvolveMatrix();
        }

        private void btnApplyColorMatrix_Click(object sender, RoutedEventArgs e)
        {
            EffectColorMatrix();
        }

        private void btnApplyDiscreteTransfer_Click(object sender, RoutedEventArgs e)
        {
            EffectDiscreteTransfer();
        }

        private void btnApplyTableTransfer_Click(object sender, RoutedEventArgs e)
        {
            EffectTableTransfer();
        }

        private void nbDivisor_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            _DivisorMatrix = (float)((NumberBox)sender).Value;
        }

        private void btnApplyAffineTransform_Click(object sender, RoutedEventArgs e)
        {
            EffectAffineTransform();
        }

        private void btnApplyTransform_Click(object sender, RoutedEventArgs e)
        {
            EffectTransform();
        }

        private void btnApplyPerspectiveTransform_Click(object sender, RoutedEventArgs e)
        {
            EffectPerspectiveTransform();
        }

        private void btnApplyAtlas_Click(object sender, RoutedEventArgs e)
        {
            EffectAtlas();
        }

        private void btnApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            EffectCrop();
        }

        private void btnApplyScale_Click(object sender, RoutedEventArgs e)
        {
            EffectScale();
        }

        private void btnApplyTile_Click(object sender, RoutedEventArgs e)
        {
            EffectTile();
        }

        private void btnApplyArithmeticComposite_Click(object sender, RoutedEventArgs e)
        {
            EffectArithmeticComposite();
        }

        private void btnApplyTurbulence_Click(object sender, RoutedEventArgs e)
        {
            EffectTurbulence();
        }

        private void rbChannelX_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is RadioButtons rb)
            {
                string sColorName = rb.SelectedItem as string;
                switch (sColorName)
                {
                    case "Alpha":
                        _ChannelX = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_A;
                        break;
                    case "Red":
                        _ChannelX = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_R;
                        break;
                    case "Green":
                        _ChannelX = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_G;
                        break;
                    case "Blue":
                        _ChannelX = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_B;
                        break;
                }
                EffectDisplacementMap();
            }
        }

        private void rbChannelY_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is RadioButtons rb)
            {
                string sColorName = rb.SelectedItem as string;
                switch (sColorName)
                {
                    case "Alpha":
                        _ChannelY = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_A;
                        break;
                    case "Red":
                        _ChannelY = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_R;
                        break;
                    case "Green":
                        _ChannelY = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_G;
                        break;
                    case "Blue":
                        _ChannelY = (uint)D2D1_CHANNEL_SELECTOR.D2D1_CHANNEL_SELECTOR_B;
                        break;
                }
                EffectDisplacementMap();
            }
        }

        // Don't save Alpha
        private async System.Threading.Tasks.Task<bool> SaveImageDialogPicker2()
        {
            var fsp = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(fsp, hWndMain);
            fsp.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            fsp.SuggestedFileName = "NewImage";

            fsp.FileTypeChoices.Add("JPG (*.jpg)", new List<string>() { ".jpg" });
            fsp.FileTypeChoices.Add("PNG Portable Network Graphics (*.png)", new List<string>() { ".png" });
            fsp.FileTypeChoices.Add("GIF Graphics Interchange Format (*.gif)", new List<string>() { ".gif" });
            fsp.FileTypeChoices.Add("BMP Windows Bitmap (*.bmp)", new List<string>() { ".bmp" });
            fsp.FileTypeChoices.Add("TIF Tagged Image File Format (*.tif)", new List<string>() { ".tif" });

            Windows.Storage.StorageFile file = await fsp.PickSaveFileAsync();
            if (file != null)
            {
                Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap renderTargetBitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await renderTargetBitmap.RenderAsync(imgEffect);
                var pixelBuffer = await renderTargetBitmap.GetPixelsAsync();
                Guid guidCodec = Guid.Empty;
                switch (file.FileType)
                {
                    case ".jpg":
                        guidCodec = Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId;
                        break;
                    case ".png":
                        guidCodec = Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId;
                        break;
                    case ".gif":
                        guidCodec = Windows.Graphics.Imaging.BitmapEncoder.GifEncoderId;
                        break;
                    case ".bmp":
                        guidCodec = Windows.Graphics.Imaging.BitmapEncoder.BmpEncoderId;
                        break;
                    case ".tif":
                        guidCodec = Windows.Graphics.Imaging.BitmapEncoder.TiffEncoderId;
                        break;
                }
                using (IRandomAccessStream ras = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(guidCodec, ras);
                    encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)renderTargetBitmap.PixelWidth, (uint)renderTargetBitmap.PixelHeight, 96.0, 96.0, pixelBuffer.ToArray());
                    //encoder.SetSoftwareBitmap(imgEffect.Source);
                    try
                    {
                        await encoder.FlushAsync();
                    }
                    catch (Exception e)
                    {
                        Windows.UI.Popups.MessageDialog md = new Windows.UI.Popups.MessageDialog("Cannot save file " + file.DisplayName + "\r\n" + "Exception : " + e.Message, "Information");
                        WinRT.Interop.InitializeWithWindow.Initialize(md, hWndMain);
                        _ = await md.ShowAsync();
                    }
                }
                return true;
            }
            else
                return false;
        }

        private async System.Threading.Tasks.Task<bool> SaveImageDialogPicker()
        {
            var fsp = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(fsp, hWndMain);
            fsp.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            fsp.SuggestedFileName = "NewImage";

            fsp.FileTypeChoices.Add("JPG (*.jpg)", new List<string>() { ".jpg" });
            fsp.FileTypeChoices.Add("PNG Portable Network Graphics (*.png)", new List<string>() { ".png" });
            fsp.FileTypeChoices.Add("GIF Graphics Interchange Format (*.gif)", new List<string>() { ".gif" });
            fsp.FileTypeChoices.Add("BMP Windows Bitmap (*.bmp)", new List<string>() { ".bmp" });
            fsp.FileTypeChoices.Add("TIF Tagged Image File Format (*.tif)", new List<string>() { ".tif" });

            Windows.Storage.StorageFile file = await fsp.PickSaveFileAsync();
            if (file != null)
            {
                SaveD2D1BitmapToFile(m_pD2DBitmapEffect, m_pD2DDeviceContext, file.Path);
                return true;
            }
            else
                return false;
        }

        private async void btnSave_Click(object sender, RoutedEventArgs e)
        {
            //m_pD2DDeviceContext.BeginDraw();
            //m_pD2DDeviceContext.Clear(new ColorF(ColorF.Enum.Red));
            //HRESULT hr = m_pD2DDeviceContext.EndDraw(out ulong tag1, out ulong tag2);
            //hr = m_pDXGISwapChain1.Present(1, 0);
            //return;

            var selectedItem = cmbEffects.SelectedItem;
            var sEffect = (selectedItem != null) ? ((Microsoft.UI.Xaml.Controls.ContentControl)selectedItem).Content.ToString() : null;
            if (sEffect != null)
                await SaveImageDialogPicker();
            else
            {
                Windows.UI.Popups.MessageDialog md = new Windows.UI.Popups.MessageDialog("No effect selected", "Information");
                WinRT.Interop.InitializeWithWindow.Initialize(md, hWndMain);
                _ = await md.ShowAsync();
            }
        }

        private void scpD2D_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Resize(e.NewSize);
        }

        HRESULT Resize(Size sz)
        {
            HRESULT hr = HRESULT.S_OK;

            if (m_pDXGISwapChain1 != null)
            {
                if (m_pD2DDeviceContext != null)
                    m_pD2DDeviceContext.SetTarget(null);

                if (m_pD2DTargetBitmap != null)
                    SafeRelease(ref m_pD2DTargetBitmap);

                // 0, 0 => HRESULT: 0x80070057 (E_INVALIDARG) if not CreateSwapChainForHwnd
                //hr = m_pDXGISwapChain1.ResizeBuffers(
                // 2,
                // 0,
                // 0,
                // DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                // 0
                // );
                if (sz.Width != 0 && sz.Height != 0)
                {
                    hr = m_pDXGISwapChain1.ResizeBuffers(
                      2,
                      (uint)sz.Width,
                      (uint)sz.Height,
                      DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                      0
                      );
                }
                ConfigureSwapChain();
            }
            return (hr);
        }

        //private DependencyObject? CollapseEffectStackPanels(DependencyObject tree)
        //{
        //    for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(tree); i++)
        //    {
        //        DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(tree, i);
        //        if (child != null && child is StackPanel && ((FrameworkElement)child).Name.Contains("Effect"))
        //            ((FrameworkElement)child).Visibility = Visibility.Collapsed;                   
        //        else
        //        {
        //            DependencyObject? childInSubtree = CollapseEffectStackPanels(child);
        //            if (childInSubtree != null)
        //                return childInSubtree;
        //        }
        //    }
        //    return null;
        //}

#nullable enable
        private DependencyObject? BuildStackPanelList(DependencyObject? tree)
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(tree); i++)
            {
                DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(tree, i);
                if (child != null && child is StackPanel && ((FrameworkElement)child).Name.Contains("Effect"))
                    listSP.Add((StackPanel)child);
                else
                {
                    DependencyObject? childInSubtree = BuildStackPanelList(child);
                    if (childInSubtree != null)
                        return childInSubtree;
                }
            }
            return null;
        }
#nullable disable

        private void CollapseEffectStackPanels()
        {
            foreach (var item in listSP)
            {
                item.Visibility = Visibility.Collapsed;
            }
        }

        private StackPanel GetCurrentEffectStackPanel()
        { 
            StackPanel currentSP = null;
            foreach (var item in listSP)
            {
                if (item.Visibility == Visibility.Visible)
                {
                    currentSP = item;
                    break;
                }
            }
            return currentSP;
        }

        void CleanDeviceResources()
        {
            SafeRelease(ref m_pD2DBitmap);
            SafeRelease(ref m_pD2DBitmap1);
            SafeRelease(ref m_pD2DBitmapTransparent1);
            SafeRelease(ref m_pD2DBitmapTransparent2);
            SafeRelease(ref m_pBitmapSourceEffect);
            SafeRelease(ref m_pD2DBitmapMask);
            SafeRelease(ref m_pD2DBitmapEffect);
            foreach (ID2D1Bitmap image in listImages)
            {
                ID2D1Bitmap imageTemp = image;
                SafeRelease(ref imageTemp);
            }
            //SafeRelease(ref m_pD2DBitmapBackground1);
        }     

        void Clean()
        {
            SafeRelease(ref m_pD2DDeviceContext);
            //SafeRelease(ref m_pD2DDeviceContext3);

            CleanDeviceResources();

            SafeRelease(ref m_pD2DTargetBitmap);
            SafeRelease(ref m_pDXGISwapChain1);

            SafeRelease(ref m_pDXGIDevice);
            SafeRelease(ref m_pD3D11DeviceContext);
            //Marshal.Release(m_pD3D11DevicePtr);

            SafeRelease(ref m_pWICImagingFactory);
            SafeRelease(ref m_pWICImagingFactory2);
            SafeRelease(ref m_pD2DFactory1);
            SafeRelease(ref m_pD2DFactory);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            Clean();
        }
    }
}
