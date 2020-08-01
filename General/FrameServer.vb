﻿
Imports System.Runtime.InteropServices

Imports StaxRip.UI

Public Class DirectFrameServer
    Implements IDisposable, IFrameServer

    Property Info As ServerInfo Implements IFrameServer.Info

    Private NativeServer As INativeFrameServer

    Sub New(path As String)
        If path.Ext = "avs" Then
            Environment.SetEnvironmentVariable("AviSynthDLL", Package.AviSynth.Path)
            NativeServer = CreateAviSynthServer()
        Else
            NativeServer = CreateVapourSynthServer()
        End If

        NativeServer.OpenFile(path)
        Info = Marshal.PtrToStructure(Of ServerInfo)(NativeServer.GetInfo())
    End Sub

    ReadOnly Property [Error] As String Implements IFrameServer.Error
        Get
            Return Marshal.PtrToStringUni(NativeServer.GetError())
        End Get
    End Property

    ReadOnly Property FrameRate As Double Implements IFrameServer.FrameRate
        Get
            Return Info.FrameRateNum / Info.FrameRateDen
        End Get
    End Property

    Function GetFrame(
        position As Integer,
        ByRef data As IntPtr,
        ByRef pitch As Integer) As Integer Implements IFrameServer.GetFrame

        Return NativeServer.GetFrame(position, data, pitch)
    End Function

    <DllImport("FrameServer.dll")>
    Shared Function CreateAviSynthServer() As INativeFrameServer
    End Function

    <DllImport("FrameServer.dll")>
    Shared Function CreateVapourSynthServer() As INativeFrameServer
    End Function

    Sub Dispose() Implements IDisposable.Dispose
        If Not NativeServer Is Nothing Then
            Marshal.ReleaseComObject(NativeServer)
            NativeServer = Nothing
        End If
    End Sub
End Class

Public Interface IFrameServer
    Inherits IDisposable

    Property Info As ServerInfo
    ReadOnly Property [Error] As String
    ReadOnly Property FrameRate As Double
    Function GetFrame(position As Integer, ByRef data As IntPtr, ByRef pitch As Integer) As Integer
End Interface

<Guid("A933B077-7EC2-42CC-8110-91DE21116C1A")>
<InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Public Interface INativeFrameServer
    <PreserveSig> Function OpenFile(file As String) As Integer
    <PreserveSig> Function GetFrame(position As Integer, ByRef data As IntPtr, ByRef pitch As Integer) As Integer
    <PreserveSig> Function GetInfo() As IntPtr
    <PreserveSig> Function GetError() As IntPtr
End Interface

Public Structure ServerInfo
    Public Width As Integer
    Public Height As Integer
    Public FrameRateNum As Integer
    Public FrameRateDen As Integer
    Public FrameCount As Integer
    Public ColorSpace As ColorSpace

    Function GetInfoText(position As Integer) As String
        Dim rate = FrameRateNum / FrameRateDen

        Dim lengthtDate = Date.Today.AddSeconds(FrameCount / rate)
        Dim dateFormat = If(lengthtDate.Hour = 0, "mm:ss.fff", "HH:mm:ss.fff")
        Dim frames = FrameCount.ToString
        Dim len = lengthtDate.ToString(dateFormat)

        If position > -1 Then
            frames = position & " of " & FrameCount
            Dim currentDate = Date.Today.AddSeconds(position / rate)
            len = currentDate.ToString(dateFormat) + " of " + lengthtDate.ToString(dateFormat)
        End If

        Return "Width     : " & Width & BR &
               "Height    : " & Height & BR &
               "Frames    : " + frames + BR +
               "Time      : " + len + BR +
               "Framerate : " + rate.ToInvariantString.Shorten(9) + " (" & FrameRateNum & "/" & FrameRateDen & ")" + BR +
               "Format    : " + ColorSpace.ToString.Replace("_", "")
    End Function
End Structure

Public Enum ColorSpace
    Unknown = 0
    BGR24 = 1342177281
    BGR32 = 1342177282
    RGBP8 = -1879048191
    RGBP10 = -1878720511
    RGBP12 = -1878654975
    RGBP14 = -1878589439
    RGBP16 = -1878982655
    Y8 = -536870912
    Y10 = -536543232
    Y12 = -536477696
    Y14 = -536412160
    Y16 = -536805376
    Y32 = -536739840
    YUV410P8 = -1610612471
    YUV411P8 = -1610611959
    YUV420P8 = -1610612720
    YUV420P8_ = -1610612728
    YUV420P10 = -1610285048
    YUV420P12 = -1610219512
    YUV420P14 = -1610153976
    YUV420P16 = -1610547192
    YUV420PS = -1610481656
    YUV422P8 = -1610611960
    YUV422P10 = -1610284280
    YUV422P12 = -1610218744
    YUV422P14 = -1610153208
    YUV422P16 = -1610546424
    YUV422PS = -1610480888
    YUV444P8 = -1610611957
    YUV444P10 = -1610284277
    YUV444P12 = -1610218741
    YUV444P14 = -1610153205
    YUV444P16 = -1610546421
    YUV444PS = -1610480885
    YUY2 = 1610612740
End Enum

Public Class FrameServerFactory
    Shared Function Create(path As String) As IFrameServer
        FrameServerHelp.Init()

        If (path.Ext = "avs" AndAlso s.AviSynthMode = FrameServerMode.VFW) OrElse
           (path.Ext = "vpy" AndAlso s.VapourSynthMode = FrameServerMode.VFW) Then

            Return New VfwFrameServer(path)
        Else
            Return New DirectFrameServer(path)
        End If
    End Function
End Class

Public Class VfwFrameServer
    Implements IDisposable, IFrameServer

    Property Info As ServerInfo Implements IFrameServer.Info

    Private AviFile As IntPtr
    Private FrameObject As IntPtr
    Private AviStream As IntPtr

    Sub New(path As String)
        Try
            Me.Error = ""
            AVIFileInit()

            If AVIFileOpen(AviFile, path, 32, IntPtr.Zero) <> 0 Then
                Throw New Exception("AVIFileOpen failed to execute")
            End If

            If AVIFileGetStream(AviFile, AviStream, mmioStringToFOURCC("vids", 0), 0) <> 0 Then
                Throw New Exception("AVIFileGetStream failed to execute")
            End If

            Dim info2 As ServerInfo
            info2.FrameCount = AVIStreamLength(AviStream)

            If info2.FrameCount = 240 Then
                Dim clipInfo = TryCast(Marshal.GetObjectForIUnknown(AviFile), IAvisynthClipInfo)

                If Not clipInfo Is Nothing Then
                    Dim ptr As IntPtr

                    If clipInfo.GetError(ptr) = 0 Then
                        Me.Error = Marshal.PtrToStringAnsi(ptr)
                    End If

                    Marshal.ReleaseComObject(clipInfo)

                    If Me.Error <> "" Then
                        Throw New Exception(Me.Error)
                    End If
                End If
            End If

            Dim aviInfo As New _AVISTREAMINFO()

            If AVIStreamInfo(AviStream, aviInfo, Marshal.SizeOf(aviInfo)) <> 0 Then
                Throw New Exception("AVIStreamInfo failed to execute")
            End If

            info2.FrameRateDen = CInt(aviInfo.dwScale)
            info2.FrameRateNum = CInt(aviInfo.dwRate)
            info2.Width = aviInfo.rcFrame.Right
            info2.Height = aviInfo.rcFrame.Bottom
            Info = info2
        Catch ex As Exception
            Me.Error = ex.Message
            Dispose()
        End Try
    End Sub

    ReadOnly Property [Error] As String Implements IFrameServer.Error

    ReadOnly Property FrameRate As Double Implements IFrameServer.FrameRate
        Get
            Return Info.FrameRateNum / Info.FrameRateDen
        End Get
    End Property

    Function GetFrame(
        position As Integer,
        ByRef data As IntPtr,
        ByRef pitch As Integer) As Integer Implements IFrameServer.GetFrame

        If FrameObject = IntPtr.Zero Then
            FrameObject = AVIStreamGetFrameOpen(AviStream, 1)
        End If

        If FrameObject <> IntPtr.Zero Then
            data = AVIStreamGetFrame(FrameObject, position)

            If data <> IntPtr.Zero Then
                data += 40
                pitch = (((Info.Width * 32) + 31) And Not 31) >> 3
                Return 0 'S_OK
            End If
        End If

        Return &H80004005 'E_FAIL
    End Function

    <Guid("E6D6B708-124D-11D4-86F3-DB80AFD98778"),
    InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Interface IAvisynthClipInfo
        Function GetError(ByRef msg As IntPtr) As Integer
        Function GetParity(value As Integer) As Byte
        Function IsFieldBased() As Byte
    End Interface

    <DllImport("avifil32.dll")>
    Shared Sub AVIFileInit()
    End Sub

    <DllImport("avifil32.dll", CharSet:=CharSet.Unicode)>
    Shared Function AVIFileOpen(
        ByRef ppfile As IntPtr, szFile As String, uMode As Integer, pclsidHandler As IntPtr) As Integer
    End Function

    <DllImport("avifil32.dll")>
    Shared Function AVIFileGetStream(
        pfile As IntPtr, ByRef ppavi As IntPtr, fccType As UInteger, lParam As Integer) As Integer
    End Function

    <DllImport("avifil32.dll")>
    Shared Function AVIStreamLength(pavi As IntPtr) As Integer
    End Function

    <DllImport("avifil32.dll", CharSet:=CharSet.Unicode)>
    Shared Function AVIStreamInfo(pAVIStream As IntPtr, ByRef psi As _AVISTREAMINFO, lSize As Integer) As Integer
    End Function

    <DllImport("avifil32.dll")>
    Shared Function AVIStreamGetFrameOpen(pAVIStream As IntPtr, lpbiWanted As Integer) As IntPtr
    End Function

    <DllImport("avifil32.dll")>
    Shared Function AVIStreamGetFrame(pGetFrameObj As IntPtr, lPos As Integer) As IntPtr
    End Function

    <DllImport("avifil32.dll")>
    Shared Function AVIStreamGetFrameClose(pGetFrameObj As IntPtr) As Integer
    End Function

    <DllImport("avifil32.dll")>
    Shared Function AVIStreamRelease(aviStream As IntPtr) As Integer
    End Function

    <DllImport("avifil32.dll")>
    Shared Function AVIFileRelease(pfile As IntPtr) As Integer
    End Function

    <DllImport("avifil32.dll")>
    Shared Sub AVIFileExit()
    End Sub

    <DllImport("winmm.dll")>
    Shared Function mmioStringToFOURCC(sz As String, uFlags As Integer) As UInteger
    End Function

    Structure BITMAPINFOHEADER
        Public biSize As UInt32
        Public biWidth As Int32
        Public biHeight As Int32
        Public biPlanes As Int16
        Public biBitCount As Int16
        Public biCompression As UInt32
        Public biSizeImage As UInt32
        Public biXPelsPerMeter As Int32
        Public biYPelsPerMeter As Int32
        Public biClrUsed As UInt32
        Public biClrImportant As UInt32
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Structure _AVISTREAMINFO
        Public fccType As UInt32
        Public fccHandler As UInt32
        Public dwFlags As UInt32
        Public dwCaps As UInt32
        Public wPriority As UInt16
        Public wLanguage As UInt16
        Public dwScale As UInt32
        Public dwRate As UInt32
        Public dwStart As UInt32
        Public dwLength As UInt32
        Public dwInitialFrames As UInt32
        Public dwSuggestedBufferSize As UInt32
        Public dwQuality As UInt32
        Public dwSampleSize As UInt32
        Public rcFrame As Native.RECT
        Public dwEditCount As UInt32
        Public dwFormatChangeCount As UInt32
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=64)>
        Public szName As String
    End Structure

    Private WasDisposed As Boolean

    Sub Dispose() Implements IDisposable.Dispose
        If Not WasDisposed Then
            If FrameObject <> IntPtr.Zero Then
                AVIStreamGetFrameClose(FrameObject)
            End If

            If AviStream <> IntPtr.Zero Then
                AVIStreamRelease(AviStream)
            End If

            If AviFile <> IntPtr.Zero Then
                AVIFileRelease(AviFile)
            End If

            AVIFileExit()
            WasDisposed = True
        End If
    End Sub
End Class

Public Class FrameServerHelp
    Shared WasInitialized As Boolean
    Shared WasAviSynthInitialized As Boolean
    Shared WasVapourSynthInitialized As Boolean

    Shared Sub Init()
        If ffmpegMightUseAviSynth() Then
            If IsAviSynthPortableUsed() Then
                Dim msg = "Soft link creation is required to use AviSynth+ in portable mode due to " +
                          "design limitations of AviSynth+ and ffmpeg." + BR2 + GetAviSynthOptions()

                MakeSoftLink("avs to ffmpeg", Package.AviSynth.Path,
                             Package.ffmpeg.Directory + "AviSynth.dll", msg)
            Else
                DeleteSoftLink(Package.ffmpeg.Directory + "AviSynth.dll")
            End If
        End If

        If Not WasInitialized Then
            g.AddToPath(Folder.Startup, Package.Python.Directory, Package.AviSynth.Directory,
                        Package.VapourSynth.Directory, Package.FFTW.Directory)

            WasInitialized = True
        End If

        If IsAviSynthUsed() AndAlso Not WasAviSynthInitialized Then
            If IsAviSynthPortableUsed() Then
                DirectoryHelp.Create(Folder.Settings + "Plugins\AviSynth")
            End If

            CreateAviSynthSoftLinks()
            WasAviSynthInitialized = True
        End If

        If IsVapourSynthUsed() AndAlso Not WasVapourSynthInitialized Then
            If IsVapourSynthPortableUsed() Then
                DirectoryHelp.Create(Folder.Settings + "Plugins\VapourSynth")
            End If

            WasVapourSynthInitialized = True
        End If
    End Sub

    Shared Function GetAviSynthOptions() As String
        Return "Option one is installing a compatible AviSynth+ version and disabling AviSynth+ " +
               "portable mode in the StaxRip settings (Tools > Settings > General)." + BR2 +
               "Option two is running StaxRip with administrative privileges until soft link " +
               "creation completes, this has to be done only once, after the links were created, " +
               "regular privileges are sufficient." + BR2 +
               "Option three is enabling Developer Mode in the Windows 10 settings, this allows " +
               "soft link creation without administrative privileges."
    End Function

    Shared Function ffmpegMightUseAviSynth() As Boolean
        Return (p.Script.Engine = ScriptEngine.AviSynth AndAlso TypeOf p.VideoEncoder Is ffmpegEnc) OrElse
            p.Audio0.File.Ext = "avs" OrElse p.Audio1.File.Ext = "avs"
    End Function

    Shared Function IsAviSynthPortableUsed() As Boolean
        Return Package.AviSynth.Directory.StartsWithEx(Folder.Apps)
    End Function

    Shared Function IsVapourSynthPortableUsed() As Boolean
        Return Package.VapourSynth.Directory.StartsWithEx(Folder.Apps)
    End Function

    Shared Function IsPortable() As Boolean
        If (IsAviSynthUsed() AndAlso IsAviSynthPortableUsed()) OrElse
            (IsVapourSynthUsed() AndAlso IsVapourSynthPortableUsed()) Then

            Return True
        End If
    End Function

    Shared Function IsAviSynthInstalled() As Boolean
        Return (Folder.System + "AviSynth.dll").FileExists
    End Function

    Shared Function IsAviSynthUsed() As Boolean
        Return p.Script.Engine = ScriptEngine.AviSynth
    End Function

    Shared Function IsVapourSynthUsed() As Boolean
        Return p.Script.Engine = ScriptEngine.VapourSynth
    End Function

    Shared Sub CreateAviSynthSoftLinks()
        Dim packs = {Package.x265, Package.NVEnc, Package.QSVEnc, Package.VCEEnc, Package.x264, Package.mpvnet}

        If IsAviSynthPortableUsed() Then
            If IsAviSynthInstalled() Then
                Dim msg = "When AviSynth+ is installed then portable mode requires " +
                          "soft link creation due to limitations of AviSynth+ and " +
                          "most AviSynth reading tools." + BR2 + GetAviSynthOptions()

                For Each pack In packs
                    MakeSoftLink("avs to " + pack.Name, Package.AviSynth.Path,
                                 pack.Directory + "AviSynth.dll", msg)
                Next
            Else
                For Each pack In packs
                    DeleteSoftLink(pack.Directory + "AviSynth.dll")
                Next
            End If
        Else
            For Each pack In packs
                DeleteSoftLink(pack.Directory + "AviSynth.dll")
            Next
        End If
    End Sub

    Shared Sub MakeSoftLink(name As String, target As String, link As String, msg As String)
        If s.Storage.GetString(name + "softlink") <> target OrElse Not link.FileExists OrElse
            New FileInfo(link).Length > 0 Then

            DeleteSoftLink(link)
            MakeSoftLink(target, link, msg)
            s.Storage.SetString(name + "softlink", target)
        End If
    End Sub

    Shared Sub MakeSoftLink(target As String, link As String, msg As String)
        'return value not working, known Windows bug
        CreateSymbolicLink(link, target, 2)

        If Not File.Exists(link) Then
            MsgError("Failed to create soft link", link + BR2 + msg)
            Throw New AbortException()
        End If
    End Sub

    Shared Sub DeleteSoftLink(path As String)
        Try
            If File.Exists(path) Then
                File.Delete(path)
            End If
        Catch ex As Exception
            MsgError("Failed to delete soft link", path + BR2 + ex.Message)
            Throw New AbortException()
        End Try
    End Sub

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode)>
    Shared Function CreateSymbolicLink(link As String, target As String, flags As Integer) As Boolean
    End Function
End Class

Public Enum FrameServerMode
    <DispName("Use portable directly")> Portable
    <DispName("Use installed directly")> Installed
    <DispName("Use installed via VFW")> VFW
End Enum
