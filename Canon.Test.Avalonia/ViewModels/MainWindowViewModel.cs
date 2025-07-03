using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Collections;
using Avalonia.Media.Imaging;
using Canon.Core;
using ReactiveUI;

namespace Canon.Test.Avalonia.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly CanonCamera _camera = new();
    private string _cameraName = "Loading...";
    private Bitmap? _liveImage;

    private string? _selectedIsoValue;
    private string? _selectedApertureValue;
    private string? _selectedWhiteBalanceValue;
    private string? _selectedShutterSpeedValue;
    private Bitmap? _takenImage;
    private string? _error;

    public string CameraName
    {
        get => _cameraName;
        private set => this.RaiseAndSetIfChanged(ref _cameraName, value);
    }

    public Bitmap? LiveImage
    {
        get => _liveImage;
        private set => Set(ref _liveImage, value);
    }

    public Bitmap? TakenImage
    {
        get => _takenImage;
        set => Set(ref _takenImage, value);
    }

    public AvaloniaList<String> IsoValues { get; } = new();

    public AvaloniaList<String> ApertureValues { get; } = new();

    public AvaloniaList<String> ShutterSpeedValues { get; } = new();

    public AvaloniaList<String> WhiteBalanceValues { get; } = new();

    public string? SelectedIsoValue
    {
        get => _selectedIsoValue;
        set => Set(ref _selectedIsoValue, value);
    }

    public string? SelectedApertureValue
    {
        get => _selectedApertureValue;
        set => Set(ref _selectedApertureValue, value);
    }

    public string? SelectedWhiteBalanceValue
    {
        get => _selectedWhiteBalanceValue;
        set => Set(ref _selectedWhiteBalanceValue, value);
    }

    public string? SelectedShutterSpeedValue
    {
        get => _selectedShutterSpeedValue;
        set => Set(ref _selectedShutterSpeedValue, value);
    }

    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    public ReactiveCommand<Unit, Unit> FocusCommand { get; }

    public ReactiveCommand<Unit, Unit> TakePictureCommand { get; }
    
    public MainWindowViewModel()
    {
        FocusCommand = ReactiveCommand.CreateFromTask(() => _camera.AutoFocus());
        
        TakePictureCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                Error = null;

                var bytes = await _camera.TakePicture();

                if (bytes != null)
                    TakenImage = new Bitmap(new MemoryStream(bytes));
            }
            catch(Exception ex)
            {
                Error = ex.Message;
            }
        });

        Observable.Start(async () =>
        {
            CameraName = await _camera.GetCameraName();
            IsoValues.AddRange(await _camera.GetSupportedValues(CameraProperty.ISOSpeed));
            ApertureValues.AddRange(await _camera.GetSupportedValues(CameraProperty.Aperture));
            ShutterSpeedValues.AddRange(await _camera.GetSupportedValues(CameraProperty.ShutterSpeed));
            WhiteBalanceValues.AddRange(await _camera.GetSupportedValues(CameraProperty.WhiteBalance));

            SelectedIsoValue = await _camera.GetValue(CameraProperty.ISOSpeed);
            SelectedWhiteBalanceValue = await _camera.GetValue(CameraProperty.WhiteBalance);
            SelectedApertureValue = await _camera.GetValue(CameraProperty.Aperture);
            SelectedShutterSpeedValue = await _camera.GetValue(CameraProperty.ShutterSpeed);
        });

        Observable.Interval(TimeSpan.FromMilliseconds(10)).Subscribe(_ => UpdateLiveView());

        this.WhenAnyValue(v => v.SelectedApertureValue)
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Where(v => v != null).Select(v => v!)
            .Subscribe(async v =>
            {
                try
                {
                    await _camera.SetValue(CameraProperty.Aperture, v);
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                }
            });

        this.WhenAnyValue(v => v.SelectedIsoValue)
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Where(v => v != null).Select(v => v!)
            .Subscribe(async v =>
            {
                try
                {
                    await _camera.SetValue(CameraProperty.ISOSpeed, v);
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                }
            });

        this.WhenAnyValue(v => v.SelectedWhiteBalanceValue)
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Where(v => v != null).Select(v => v!)
            .Subscribe(async v =>
            {
                try
                {
                    await _camera.SetValue(CameraProperty.WhiteBalance, v);
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                }
            });

        this.WhenAnyValue(v => v.SelectedShutterSpeedValue)
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Where(v => v != null).Select(v => v!)
            .Subscribe(async v =>
            {
                try
                {
                    await _camera.SetValue(CameraProperty.ShutterSpeed, v);
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                }
            });
    }

    private void UpdateLiveView()
    {
        var bytes = _camera.GetLiveView().Result;

        if (bytes != null) 
            LiveImage = new Bitmap(new MemoryStream(bytes));
    }
}