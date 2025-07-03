using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Media.Imaging;
using Canon.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Canon.Test.Avalonia.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly CanonCamera _camera = new();

    public AvaloniaList<String> IsoValues { get; } = new();
    
    public AvaloniaList<String> ApertureValues { get; } = new();

    public AvaloniaList<String> ShutterSpeedValues { get; } = new();

    public AvaloniaList<String> WhiteBalanceValues { get; } = new();

    [Reactive] public string CameraName { get; private set; } = "Loading...";

    [Reactive] public Bitmap? LiveImage { get; private set; }

    [Reactive] public Bitmap? TakenImage { get; private set; }

    [Reactive] public bool AutoFocus { get; set; } = true;

    [Reactive] public string? Iso { get; set; }

    [Reactive] public string? Aperture { get; set; }

    [Reactive] public string? WhiteBalance { get; set; }

    [Reactive] public string? ShutterSpeed { get; set; }

    [Reactive] public string? Error { get; private set; }

    public ReactiveCommand<Unit, Unit> FocusCommand { get; }

    public ReactiveCommand<Unit, Unit> TakePictureCommand { get; }

    public MainWindowViewModel()
    {
        FocusCommand = ReactiveCommand.CreateFromTask(() => _camera.AutoFocus());

        TakePictureCommand = ReactiveCommand.CreateFromTask(TakePicture);

        Observable.Start(async () => await StartCamera());

        Observable.Timer(TimeSpan.FromMilliseconds(500)).Subscribe(async _ => await UpdateLiveView());

        new List<(CameraProperty property, Expression<Func<MainWindowViewModel, string?>> selector)>
            {
                (CameraProperty.ISOSpeed, v => v.Iso),
                (CameraProperty.Aperture, v => v.Aperture),
                (CameraProperty.ShutterSpeed, v => v.ShutterSpeed),
                (CameraProperty.WhiteBalance, v => v.WhiteBalance)
            }
            .ForEach(x => this.WhenAnyValue(x.selector)
                .DistinctUntilChanged()
                .Throttle(TimeSpan.FromMilliseconds(300))
                .Where(v => v != null).Select(v => v!)
                .Subscribe(async v =>
                {
                    try
                    {
                        await _camera.SetValue(x.property, v);
                    }
                    catch (Exception ex)
                    {
                        Error = ex.Message;
                    }
                }));
    }

    private async Task TakePicture()
    {
        try
        {
            Error = null;

            var bytes = await _camera.TakePicture(AutoFocus);

            if (bytes != null) TakenImage = new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private async Task StartCamera()
    {
        try
        {
            CameraName = await _camera.GetCameraName();

            IsoValues.AddRange(await _camera.GetSupportedValues(CameraProperty.ISOSpeed));
            ApertureValues.AddRange(await _camera.GetSupportedValues(CameraProperty.Aperture));
            ShutterSpeedValues.AddRange(await _camera.GetSupportedValues(CameraProperty.ShutterSpeed));
            WhiteBalanceValues.AddRange(await _camera.GetSupportedValues(CameraProperty.WhiteBalance));

            Iso = await _camera.GetValue(CameraProperty.ISOSpeed);
            WhiteBalance = await _camera.GetValue(CameraProperty.WhiteBalance);
            Aperture = await _camera.GetValue(CameraProperty.Aperture);
            ShutterSpeed = await _camera.GetValue(CameraProperty.ShutterSpeed);

            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Observable.Timer(TimeSpan.FromSeconds(1)).Subscribe(async _ => await StartCamera());
        }
    }

    private async Task UpdateLiveView()
    {
        try
        {
            var bytes = await _camera.GetLiveView();

            if (bytes != null)
                LiveImage = new Bitmap(new MemoryStream(bytes));

            Observable.Timer(TimeSpan.FromMilliseconds(30)).Subscribe(async _ => await UpdateLiveView());
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            LiveImage = null;
            Observable.Timer(TimeSpan.FromMilliseconds(100)).Subscribe(async _ => await StartCamera());
            Observable.Timer(TimeSpan.FromMilliseconds(500)).Subscribe(async _ => await UpdateLiveView());
        }
    }
}