// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Business Source License 1.1
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms, including Additional Use Grant.
// =============================================================================

using GenerallyPositive.Browser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    // Configuration class for Google Drive API settings
    public class GoogleDriveConfig
    {
        public string DriveApiBase { get; set; } = "https://www.googleapis.com/drive";
        public string FilesEndpoint { get; set; } = "/v3/files";
        public string UploadEndpoint { get; set; } = "/upload/drive/v3/files";
        public string AuthorizationHeader { get; set; } = "Authorization";
        public string Bearer { get; set; } = "Bearer";
        public string QueryFields { get; set; } = "id,name,mimeType,parents";
        public string UploadType { get; set; } = "?uploadType=multipart";
    }

    // Interfaces for fluent API
    public interface IAllowDriveCredentials
    {
        [Settings(Description = "The Google account to use Drive as")]
        IAllowDriveFileSelection WithCredentials(ICredentials credentials);
    }

    public interface IAllowDriveFileSelection : IAllowToGPALObject<IGoogleDrive>
    {
        [Settings(Description = "The file on this machine to upload")]
        IAllowLocalFileOperations WithLocalFile(GPALFile file);
        [Settings(Description = "The Drive file to work on, by id")]
        IAllowRemoteFileOperations WithFileId(string fileId);
        [Settings(Description = "The Drive file to work on, by name")]
        IAllowRemoteFileOperations WithFileName(string name);
    }

    public interface IAllowLocalFileOperations
    {
        [Action(Description = "Upload the file to the Drive folder")]
        IAllowDriveActions UploadTo([FilePath] string fileName);
    }

    public interface IAllowRemoteFileOperations
    {
        [Action(Description = "Download the Drive file to this machine")]
        IAllowDriveActions SaveTo(GPALFile destination);
        [File(Description = "Delete the Drive file")]
        IAllowDriveActions DeleteFile();
        [Action(Description = "Move the Drive file to another folder")]
        IAllowDriveActions MoveTo(string folderId);
        [File(Description = "Rename the Drive file")]
        IAllowDriveActions RenameTo(string newName);
        [Settings(Description = "The ids of every Drive file with the chosen name")]
        IAllowDriveListOperations WithFileIds(out IGPALGrid<string> fileIds);
        [Action(Description = "List the files in Drive")]
        IAllowDriveListOperations ListFiles(out IGPALGrid<string> files);
    }

    public interface IAllowDriveActions : IAllowDriveFileSelection
    {
    }

    public interface IAllowDriveListOperations : IAllowDriveFileSelection
    {
    }

    public interface IGoogleDrive : IAllowDriveCredentials, IAllowDriveFileSelection, IAllowLocalFileOperations, IAllowRemoteFileOperations, IAllowDriveActions, IAllowDriveListOperations
    {
    }

    // Main GoogleDrive class
    public class GoogleDrive : IGoogleDrive
    {
        internal GoogleDriveConfig Config { get; private set; }
        internal IRESTClient DriveRESTClient { get; private set; }
        internal ICredentials Credentials { get; private set; }
        internal GPALFile LocalFile { get; private set; }
        internal string FileId { get; private set; }
        internal string FileName { get; private set; }

        internal GoogleDrive()
        {
            Config = new GoogleDriveConfig();
            DriveRESTClient = GPAL.RESTClient.WithAPIBase(Config.DriveApiBase).ToGPALObject();
        }

        public IAllowDriveFileSelection WithCredentials(ICredentials credentials)
        {
            Credentials = credentials;
            return this;
        }

        public IAllowLocalFileOperations WithLocalFile(GPALFile file)
        {
            LocalFile = file;
            return this;
        }

        public IAllowRemoteFileOperations WithFileId(string fileId)
        {
            FileId = fileId;
            return this;
        }

        public IAllowRemoteFileOperations WithFileName(string name)
        {
            FileName = name;
            return this;
        }

        private string GetToken()
        {
            Credentials.FetchAccessToken(out string token);
            return token;
        }

        private string ResolveFileId()
        {
            if (false == string.IsNullOrEmpty(FileId))
                return FileId;

            if (string.IsNullOrEmpty(FileName))
                return null;

            var query = $"name='{FileName}' and trashed=false";
            var response = DriveRESTClient
                .WithEndpoint($"{Config.FilesEndpoint}?q={Uri.EscapeDataString(query)}&fields=files(id)")
                .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                .Execute<DriveFilesResponse>();

            return response?.Files?.FirstOrDefault()?.Id;
        }

        public IAllowDriveActions UploadTo(string fileName)
        {
            if (LocalFile == null || LocalFile.Filenames.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No local file specified for upload", null, GPALObjectType.None);
                return this;
            }

            string filePath = LocalFile.Filenames.First();

            FileName = fileName;
            string existingId = ResolveFileId();
            if (existingId != null)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"A file named [{fileName}] already exists in Drive (id: [{existingId}]). Drive will create a duplicate.", null, GPALObjectType.None);
            FileName = null;

            try
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent($"{{ \"name\": \"{fileName}\" }}"), "metadata", "application/json");
                content.Add(new ByteArrayContent(File.ReadAllBytes(filePath)), "file", fileName);

                var response = DriveRESTClient
                    .WithEndpoint($"{Config.UploadEndpoint}{Config.UploadType}")
                    .WithParameters(content)
                    .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                    .Execute<DriveFileResponse>();

                if (response == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to upload file [{filePath}]", null, GPALObjectType.None);
                }
                else
                {
                    FileId = response.Id;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to upload file [{filePath}]", null, GPALObjectType.None, ex);
            }

            return this;
        }

        public IAllowDriveActions SaveTo(GPALFile destination)
        {
            var resolvedId = ResolveFileId();
            if (string.IsNullOrEmpty(resolvedId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No file specified for download", null, GPALObjectType.None);
                return this;
            }

            if (destination == null || destination.Filenames.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No destination file specified for download", null, GPALObjectType.None);
                return this;
            }

            string destinationPath = destination.Filenames.First();
            try
            {
                var bytes = DriveRESTClient
                    .WithEndpoint($"{Config.FilesEndpoint}/{resolvedId}?alt=media")
                    .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                    .Execute<byte[]>();

                if (bytes != null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{bytes.Length}] bytes of drive file [{resolvedId}] to [{destinationPath}].", null, GPALObjectType.None);
                    FileHelper.EnsureFolderFor(destinationPath);
                    File.WriteAllBytes(destinationPath, bytes);
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to download file [{resolvedId}]", null, GPALObjectType.None);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to download file [{resolvedId}]", null, GPALObjectType.None, ex);
            }

            return this;
        }

        public IAllowDriveActions DeleteFile()
        {
            var resolvedId = ResolveFileId();
            if (string.IsNullOrEmpty(resolvedId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No file specified for deletion", null, GPALObjectType.None);
                return this;
            }

            var response = DriveRESTClient
                .WithEndpoint($"{Config.FilesEndpoint}/{resolvedId}")
                .WithHttpMethod("DELETE")
                .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                .Execute();

            if (response == null)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to delete file [{resolvedId}]", null, GPALObjectType.None);

            FileId = null;
            return this;
        }

        public IAllowDriveActions RenameTo(string newName)
        {
            var resolvedId = ResolveFileId();
            if (string.IsNullOrEmpty(resolvedId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No file specified for rename", null, GPALObjectType.None);
                return this;
            }

            if (string.IsNullOrEmpty(newName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No new file name specified for rename", null, GPALObjectType.None);
                return this;
            }

            try
            {
                var response = DriveRESTClient
                    .WithEndpoint($"{Config.FilesEndpoint}/{resolvedId}")
                    .WithParameters(new { name = newName })
                    .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                    .WithHttpMethod("PATCH")
                    .Execute<DriveFileResponse>();

                if (response == null)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to rename file [{resolvedId}] to [{newName}]", null, GPALObjectType.None);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to rename file [{resolvedId}]", null, GPALObjectType.None, ex);
            }

            return this;
        }

        public IAllowDriveActions MoveTo(string folderId)
        {
            var resolvedId = ResolveFileId();
            if (string.IsNullOrEmpty(resolvedId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No file specified for move", null, GPALObjectType.None);
                return this;
            }

            if (string.IsNullOrEmpty(folderId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No folder ID specified for move", null, GPALObjectType.None);
                return this;
            }

            try
            {
                var getResponse = DriveRESTClient
                    .WithEndpoint($"{Config.FilesEndpoint}/{resolvedId}?fields=parents")
                    .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                    .Execute<DriveFileResponse>();

                var currentParents = getResponse?.Parents ?? new List<string>();

                var response = DriveRESTClient
                    .WithEndpoint($"{Config.FilesEndpoint}/{resolvedId}?addParents={folderId}&removeParents={string.Join(",", currentParents)}")
                    .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                    .WithHttpMethod("PATCH")
                    .Execute<DriveFileResponse>();

                if (response == null)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to move file [{resolvedId}] to folder [{folderId}]", null, GPALObjectType.None);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to move file [{resolvedId}]", null, GPALObjectType.None, ex);
            }

            return this;
        }

        public IAllowDriveListOperations WithFileIds(out IGPALGrid<string> fileIds)
        {
            fileIds = GPAL.GridForType<string>();

            if (string.IsNullOrEmpty(FileName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No file name specified for getting IDs", null, GPALObjectType.None);
                return this;
            }

            try
            {
                var query = $"name='{FileName}' and trashed=false";
                var response = DriveRESTClient
                    .WithEndpoint($"{Config.FilesEndpoint}?q={Uri.EscapeDataString(query)}&fields=files({Config.QueryFields})")
                    .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                    .Execute<DriveFilesResponse>();

                if (response?.Files != null)
                    foreach (var file in response.Files)
                        fileIds.AddRow(new List<string> { file.Id });
                else
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to get IDs for file name [{FileName}]", null, GPALObjectType.None);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to get IDs for file name [{FileName}]", null, GPALObjectType.None, ex);
            }

            return this;
        }

        public IAllowDriveListOperations ListFiles(out IGPALGrid<string> files)
        {
            files = GPAL.GridForType<string>();

            try
            {
                var response = DriveRESTClient
                    .WithEndpoint($"{Config.FilesEndpoint}?fields=files({Config.QueryFields})")
                    .WithHeader(Config.AuthorizationHeader, $"{Config.Bearer} {GetToken()}")
                    .Execute<DriveFilesResponse>();

                if (response?.Files != null)
                {
                    foreach (var file in response.Files)
                    {
                        files.AddRow(new List<string> { file.Id, file.Name, file.MimeType, string.Join(",", file.Parents ?? new List<string>()) });
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to list files", null, GPALObjectType.None);
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to list files", null, GPALObjectType.None, ex);
            }

            return this;
        }

        public IGoogleDrive ToGPALObject()
        {
            return this;
        }
    }

    // Response classes for API calls
    internal class DriveFilesResponse
    {
        public List<DriveFileResponse> Files { get; set; }
    }

    internal class DriveFileResponse
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string MimeType { get; set; }
        public List<string> Parents { get; set; }
    }
}
