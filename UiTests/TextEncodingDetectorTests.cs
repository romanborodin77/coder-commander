using System.Text;
using CoderCommander.Services;

namespace CoderCommander.UiTests;

public class TextEncodingDetectorTests
{
    [Test]
    public void Utf8WithBom_DetectedAndPreambleSkipped()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
        var encoding = TextEncodingDetector.Detect(bytes, out var preambleLength);

        Assert.That(preambleLength, Is.EqualTo(3));
        Assert.That(encoding.GetPreamble().Length, Is.EqualTo(3), "Should round-trip as UTF-8 WITH BOM on save");
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        Assert.That(text, Is.EqualTo("hi"));
    }

    [Test]
    public void Utf8WithoutBom_DetectedAsNoBom()
    {
        var bytes = Encoding.UTF8.GetBytes("hello");
        var encoding = TextEncodingDetector.Detect(bytes, out var preambleLength);

        Assert.That(preambleLength, Is.EqualTo(0));
        Assert.That(encoding.GetPreamble().Length, Is.EqualTo(0), "Must not add a BOM that wasn't there originally");
        Assert.That(encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength), Is.EqualTo("hello"));
    }

    [Test]
    public void Utf16LittleEndian_Detected()
    {
        // GetBytes() alone never includes the preamble - only GetPreamble()/StreamWriter/
        // File.WriteAllText do, so a real file on disk needs it prepended explicitly here too.
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hi")).ToArray();
        var encoding = TextEncodingDetector.Detect(bytes, out var preambleLength);

        Assert.That(preambleLength, Is.EqualTo(2));
        Assert.That(encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength), Is.EqualTo("hi"));
    }

    [Test]
    public void Utf16BigEndian_Detected()
    {
        var bytes = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("hi")).ToArray();
        var encoding = TextEncodingDetector.Detect(bytes, out var preambleLength);

        Assert.That(preambleLength, Is.EqualTo(2));
        Assert.That(encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength), Is.EqualTo("hi"));
    }

    [Test]
    public void RoundTrip_NoBomFileStaysNoBomAfterSave()
    {
        // Simulates EditorTab.LoadFile -> SaveFile for a plain UTF-8-without-BOM source file
        // (e.g. a .sh script) - a prior bug forced Encoding.UTF8 (which emits a BOM) on every save.
        var original = Encoding.UTF8.GetBytes("#!/bin/sh\necho hi\n");
        var encoding = TextEncodingDetector.Detect(original, out var preambleLength);
        var text = encoding.GetString(original, preambleLength, original.Length - preambleLength);

        var resaved = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

        Assert.That(resaved, Is.EqualTo(original), "Re-saving an unmodified no-BOM file must not introduce a BOM");
    }

    [Test]
    public void RoundTrip_BomFileKeepsBomAfterSave()
    {
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("test content")).ToArray();
        var encoding = TextEncodingDetector.Detect(original, out var preambleLength);
        var text = encoding.GetString(original, preambleLength, original.Length - preambleLength);

        var resaved = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

        Assert.That(resaved, Is.EqualTo(original), "Re-saving an unmodified BOM file must keep exactly one BOM, not zero or two");
    }
}
