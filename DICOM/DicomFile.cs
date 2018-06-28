﻿// Copyright (c) 2012-2018 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System;
using System.IO;
using System.Text;

#if !NET35
using System.Threading.Tasks;
#endif

using Dicom.IO;
using Dicom.IO.Reader;
using Dicom.IO.Writer;

namespace Dicom
{

    /// <summary>
    /// Container class for DICOM file parsing states.
    /// </summary>
    public sealed class ParseState
    {
        #region PROPERTIES

        /// <summary>
        /// Gets or sets the DICOM tag associated with the parse state.
        /// </summary>
        public DicomTag Tag { get; set; }

        /// <summary>
        /// Gets or sets the sequence depth (zero-based) associated with the parse state.
        /// </summary>
        public int SequenceDepth { get; set; }

        #endregion
    }

    /// <summary>
    /// Option for reading a DICOM file from a stream
    /// </summary>
    public enum FileReadOption
    {
        /// <summary>
        /// Reads only small tags, but keeps the stream to read the large tags on demand.
        /// The stream has to stay open.
        /// </summary>
        LargeOnDemand,
        /// <summary>
        /// Large tags are not read. The stream can be closed.
        /// </summary>
        SkipLargeTags,
        /// <summary>
        /// Read all tags so that the stream can be closed.
        /// </summary>
        ReadAll
    }

    /// <summary>
    /// Representation of one DICOM file.
    /// </summary>
    public class DicomFile
    {

        #region CONSTRUCTORS

        public DicomFile()
        {
            FileMetaInfo = new DicomFileMetaInformation();
            Dataset = new DicomDataset();
            Format = DicomFileFormat.DICOM3;
            IsPartial = false;
        }

        public DicomFile(DicomDataset dataset)
        {
            Dataset = dataset;
            FileMetaInfo = new DicomFileMetaInformation(Dataset);
            Format = DicomFileFormat.DICOM3;
            IsPartial = false;
        }

        #endregion

        #region PROPERTIES

        /// <summary>
        /// Gets the file reference of the DICOM file.
        /// </summary>
        public IFileReference File { get; protected set; }

        /// <summary>
        /// Gets the DICOM file format.
        /// </summary>
        public DicomFileFormat Format { get; protected set; }

        /// <summary>
        /// Gets the DICOM file meta information of the file.
        /// </summary>
        public DicomFileMetaInformation FileMetaInfo { get; protected set; }

        /// <summary>
        /// Gets the DICOM dataset of the file.
        /// </summary>
        public DicomDataset Dataset { get; protected set; }

        /// <summary>
        /// Gets whether the parsing of the file ended prematurely.
        /// </summary>
        public bool IsPartial { get; protected set; }

        #endregion

        #region METHODS

        /// <summary>
        /// Save DICOM file.
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <param name="options">Options to apply during writing.</param>
        public void Save(string fileName, DicomWriteOptions options = null)
        {
            PreprocessFileMetaInformation();

            File = IOManager.CreateFileReference(fileName);
            File.Delete();

            OnSave();

            using (var target = new FileByteTarget(File))
            {
                var writer = new DicomFileWriter(options);
                writer.Write(target, FileMetaInfo, Dataset);
            }
        }

        /// <summary>
        /// Save DICOM file to stream.
        /// </summary>
        /// <param name="stream">Stream on which to save DICOM file.</param>
        /// <param name="options">Options to apply during writing.</param>
        public void Save(Stream stream, DicomWriteOptions options = null)
        {
            PreprocessFileMetaInformation();
            OnSave();

            var target = new StreamByteTarget(stream);
            var writer = new DicomFileWriter(options);
            writer.Write(target, FileMetaInfo, Dataset);
        }

#if !NET35
        /// <summary>
        /// Save to file asynchronously.
        /// </summary>
        /// <param name="fileName">Name of file.</param>
        /// <param name="options">Options to apply during writing.</param>
        /// <returns>Awaitable <see cref="Task"/>.</returns>
        public async Task SaveAsync(string fileName, DicomWriteOptions options = null)
        {
            PreprocessFileMetaInformation();

            File = IOManager.CreateFileReference(fileName);
            File.Delete();

            OnSave();

            using (var target = new FileByteTarget(File))
            {
                var writer = new DicomFileWriter(options);
                await writer.WriteAsync(target, FileMetaInfo, Dataset).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Asynchronously save DICOM file to stream.
        /// </summary>
        /// <param name="stream">Stream on which to save DICOM file.</param>
        /// <param name="options">Options to apply during writing.</param>
        /// <returns>Awaitable task.</returns>
        public async Task SaveAsync(Stream stream, DicomWriteOptions options = null)
        {
            PreprocessFileMetaInformation();
            OnSave();

            var target = new StreamByteTarget(stream);
            var writer = new DicomFileWriter(options);
            await writer.WriteAsync(target, FileMetaInfo, Dataset).ConfigureAwait(false);
        }
#endif

        /// <summary>
        /// Reads the specified filename and returns a DicomFile object.  Note that the values for large
        /// DICOM elements (e.g. PixelData) are read in "on demand" to conserve memory.  Large DICOM elements
        /// are determined by their size in bytes - see the default value for this in the FileByteSource._largeObjectSize
        /// </summary>
        /// <param name="fileName">The filename of the DICOM file</param>
        /// <param name="readOption">An option how to deal with large dicom tags like pixel data.</param>
        /// <returns>DicomFile instance</returns>
        public static DicomFile Open(string fileName, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            return Open(fileName, DicomEncoding.Default, readOption: readOption);
        }

        /// <summary>
        /// Reads the specified filename and returns a DicomFile object.  Note that the values for large
        /// DICOM elements (e.g. PixelData) are read in "on demand" to conserve memory.  Large DICOM elements
        /// are determined by their size in bytes - see the default value for this in the FileByteSource._largeObjectSize
        /// </summary>
        /// <param name="fileName">The filename of the DICOM file</param>
        /// <param name="fallbackEncoding">Encoding to apply when attribute Specific Character Set is not available.</param>
        /// <param name="stop">Stop criterion in dataset.</param>
        /// <returns>DicomFile instance</returns>
        public static DicomFile Open(string fileName, Encoding fallbackEncoding, Func<ParseState, bool> stop = null, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            if (fallbackEncoding == null)
            {
                throw new ArgumentNullException(nameof(fallbackEncoding));
            }
            DicomFile df = new DicomFile();

            try
            {
                df.File = IOManager.CreateFileReference(fileName);

                using (var source = new FileByteSource(df.File, readOption))
                {
                    var reader = new DicomFileReader();
                    var result = reader.Read(
                        source,
                        new DicomDatasetReaderObserver(df.FileMetaInfo),
                        new DicomDatasetReaderObserver(df.Dataset, fallbackEncoding),
                        stop);

                    if (result == DicomReaderResult.Processing)
                    {
                        throw new DicomFileException(df, "Invalid read return state: {state}", result);
                    }
                    if (result == DicomReaderResult.Error)
                    {
                        return null;
                    }
                    df.IsPartial = result == DicomReaderResult.Stopped || result == DicomReaderResult.Suspended;

                    df.Format = reader.FileFormat;

                    df.Dataset.InternalTransferSyntax = reader.Syntax;

                    return df;
                }
            }
            catch (Exception e)
            {
                throw new DicomFileException(df, e.Message, e);
            }
        }

        /// <summary>
        /// Read a DICOM file from stream.
        /// </summary>
        /// <param name="stream">Stream to read.</param>
        /// <param name="readOption">The option how to deal with large DICOM tags like pixel data.</param>
        /// <returns>Read <see cref="DicomFile"/>.</returns>
        public static DicomFile Open(Stream stream, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            return Open(stream, DicomEncoding.Default, readOption: readOption);
        }

        /// <summary>
        /// Read a DICOM file from stream.
        /// </summary>
        /// <param name="stream">Stream to read.</param>
        /// <param name="fallbackEncoding">Encoding to use if encoding cannot be obtained from DICOM file.</param>
        /// <param name="stop">Stop criterion in dataset.</param>
        /// <param name="readOption">The option how to deal with large DICOM tag like pixel data</param>
        /// <returns>Read <see cref="DicomFile"/>.</returns>
        public static DicomFile Open(Stream stream, Encoding fallbackEncoding, Func<ParseState, bool> stop = null, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            if (fallbackEncoding == null)
            {
                throw new ArgumentNullException(nameof(fallbackEncoding));
            }
            var df = new DicomFile();

            try
            {
                var source = new StreamByteSource(stream, readOption);

                var reader = new DicomFileReader();
                var result = reader.Read(
                    source,
                    new DicomDatasetReaderObserver(df.FileMetaInfo),
                    new DicomDatasetReaderObserver(df.Dataset, fallbackEncoding),
                    stop);

                if (result == DicomReaderResult.Processing)
                {
                    throw new DicomFileException(df, "Invalid read return state: {state}", result);
                }
                if (result == DicomReaderResult.Error)
                {
                    return null;
                }
                df.IsPartial = result == DicomReaderResult.Stopped || result == DicomReaderResult.Suspended;

                df.Format = reader.FileFormat;

                df.Dataset.InternalTransferSyntax = reader.Syntax;

                return df;
            }
            catch (Exception e)
            {
                throw new DicomFileException(df, e.Message, e);
            }
        }

#if !NET35
        /// <summary>
        /// Asynchronously reads the specified filename and returns a DicomFile object.  Note that the values for large
        /// DICOM elements (e.g. PixelData) are read in "on demand" to conserve memory.  Large DICOM elements
        /// are determined by their size in bytes - see the default value for this in the FileByteSource._largeObjectSize
        /// </summary>
        /// <param name="fileName">The filename of the DICOM file</param>
        /// <param name="readOption">The option how to deal with large dicom tags like pixel data.</param>
        /// <returns>Awaitable <see cref="DicomFile"/> instance.</returns>
        public static Task<DicomFile> OpenAsync(string fileName, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            return OpenAsync(fileName, DicomEncoding.Default, readOption: readOption);
        }

        /// <summary>
        /// Asynchronously reads the specified filename and returns a DicomFile object.  Note that the values for large
        /// DICOM elements (e.g. PixelData) are read in "on demand" to conserve memory.  Large DICOM elements
        /// are determined by their size in bytes - see the default value for this in the FileByteSource._largeObjectSize
        /// </summary>
        /// <param name="fileName">The filename of the DICOM file</param>
        /// <param name="fallbackEncoding">Encoding to apply when attribute Specific Character Set is not available.</param>
        /// <param name="stop">Stop criterion in dataset.</param>
        /// <returns>Awaitable <see cref="DicomFile"/> instance.</returns>
        public static async Task<DicomFile> OpenAsync(string fileName, Encoding fallbackEncoding, Func<ParseState, bool> stop = null, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            if (fallbackEncoding == null)
            {
                throw new ArgumentNullException(nameof(fallbackEncoding));
            }
            var df = new DicomFile();

            try
            {
                df.File = IOManager.CreateFileReference(fileName);

                using (var source = new FileByteSource(df.File, readOption))
                {
                    var reader = new DicomFileReader();
                    var result =
                        await
                        reader.ReadAsync(
                            source,
                            new DicomDatasetReaderObserver(df.FileMetaInfo),
                            new DicomDatasetReaderObserver(df.Dataset, fallbackEncoding),
                            stop).ConfigureAwait(false);

                    if (result == DicomReaderResult.Processing)
                    {
                        throw new DicomFileException(df, "Invalid read return state: {state}", result);
                    }
                    if (result == DicomReaderResult.Error)
                    {
                        return null;
                    }
                    df.IsPartial = result == DicomReaderResult.Stopped || result == DicomReaderResult.Suspended;

                    df.Format = reader.FileFormat;
                    df.Dataset.InternalTransferSyntax = reader.Syntax;

                    return df;
                }
            }
            catch (Exception e)
            {
                throw new DicomFileException(df, e.Message, e);
            }
        }

        /// <summary>
        /// Asynchronously read a DICOM file from stream.
        /// </summary>
        /// <param name="stream">Stream to read.</param>
        /// <param name="readOption">The option how to deal with large DICOM tags like pixel data.</param>
        /// <returns>Awaitable <see cref="DicomFile"/> instance.</returns>
        public static Task<DicomFile> OpenAsync(Stream stream, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            return OpenAsync(stream, DicomEncoding.Default, readOption: readOption);
        }

        /// <summary>
        /// Asynchronously read a DICOM file from stream.
        /// </summary>
        /// <param name="stream">Stream to read.</param>
        /// <param name="fallbackEncoding">Encoding to use if encoding cannot be obtained from DICOM file.</param>
        /// <param name="stop">Stop criterion in dataset.</param>
        /// <param name="readOption">The option how to deal with large DICOM tags like pixel data.</param>
        /// <returns>Awaitable <see cref="DicomFile"/> instance.</returns>
        public static async Task<DicomFile> OpenAsync(Stream stream, Encoding fallbackEncoding, Func<ParseState, bool> stop = null, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            if (fallbackEncoding == null)
            {
                throw new ArgumentNullException(nameof(fallbackEncoding));
            }
            var df = new DicomFile();

            try
            {
                var source = new StreamByteSource(stream, readOption);

                var reader = new DicomFileReader();
                var result =
                    await
                    reader.ReadAsync(
                        source,
                        new DicomDatasetReaderObserver(df.FileMetaInfo),
                        new DicomDatasetReaderObserver(df.Dataset, fallbackEncoding),
                        stop).ConfigureAwait(false);

                if (result == DicomReaderResult.Processing)
                {
                    throw new DicomFileException(df, "Invalid read return state: {state}", result);
                }
                if (result == DicomReaderResult.Error)
                {
                    return null;
                }
                df.IsPartial = result == DicomReaderResult.Stopped || result == DicomReaderResult.Suspended;

                df.Format = reader.FileFormat;
                df.Dataset.InternalTransferSyntax = reader.Syntax;

                return df;
            }
            catch (Exception e)
            {
                throw new DicomFileException(df, e.Message, e);
            }
        }
#endif

        /// <summary>
        /// Test if file has a valid preamble and DICOM 3.0 header.
        /// </summary>
        /// <param name="path">Path to file</param>
        /// <returns>True if valid DICOM 3.0 file header is detected.</returns>
        public static bool HasValidHeader(string path)
        {
            try
            {
                var file = IOManager.CreateFileReference(path);
                using (var fs = file.OpenRead())
                {
                    fs.Seek(128, SeekOrigin.Begin);
                    return fs.ReadByte() == 'D' && fs.ReadByte() == 'I' && fs.ReadByte() == 'C' && fs.ReadByte() == 'M';
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        public override string ToString()
        {
            return string.Format("DICOM File [{0}]", this.Format);
        }

        /// <summary>
        /// Reads the specified file and returns a DicomFile object.  Note that the values for large
        /// DICOM elements (e.g. PixelData) are read in "on demand" to conserve memory.  Large DICOM elements
        /// are determined by their size in bytes - see the default value for this in the FileByteSource._largeObjectSize
        /// </summary>
        /// <param name="file">The file reference of the DICOM file</param>
        /// <param name="fallbackEncoding">Encoding to apply when attribute Specific Character Set is not available.</param>
        /// <returns>DicomFile instance</returns>
        internal static DicomFile Open(IFileReference file, Encoding fallbackEncoding, FileReadOption readOption = FileReadOption.LargeOnDemand)
        {
            if (fallbackEncoding == null)
            {
                throw new ArgumentNullException(nameof(fallbackEncoding));
            }
            DicomFile df = new DicomFile();

            try
            {
                df.File = file;

                using (var source = new FileByteSource(file, readOption))
                {
                    DicomFileReader reader = new DicomFileReader();
                    var result = reader.Read(
                        source,
                        new DicomDatasetReaderObserver(df.FileMetaInfo),
                        new DicomDatasetReaderObserver(df.Dataset, fallbackEncoding));

                    if (result == DicomReaderResult.Processing)
                    {
                        throw new DicomFileException(df, "Invalid read return state: {state}", result);
                    }
                    if (result == DicomReaderResult.Error)
                    {
                        return null;
                    }
                    df.IsPartial = result == DicomReaderResult.Stopped || result == DicomReaderResult.Suspended;

                    df.Format = reader.FileFormat;

                    df.Dataset.InternalTransferSyntax = reader.Syntax;

                    return df;
                }
            }
            catch (Exception e)
            {
                throw new DicomFileException(df, e.Message, e);
            }
        }

        /// <summary>
        /// Method to call before performing the actual saving.
        /// </summary>
        protected virtual void OnSave()
        {
        }

        /// <summary>
        /// Preprocess file meta information before save.
        /// </summary>
        /// <exception cref="DicomFileException">If file format is ACR-NEMA version 2 or 3.</exception>
        private void PreprocessFileMetaInformation()
        {
            if (this.Format == DicomFileFormat.ACRNEMA1 || this.Format == DicomFileFormat.ACRNEMA2)
            {
                throw new DicomFileException(this, "Unable to save ACR-NEMA file");
            }

            // create file meta information from dataset or update existing file meta information.
            this.FileMetaInfo = this.Format == DicomFileFormat.DICOM3NoFileMetaInfo
                                    ? new DicomFileMetaInformation(this.Dataset)
                                    : new DicomFileMetaInformation(this.FileMetaInfo);
        }

        #endregion
    }
}
