﻿using Common;
using Common.Database;
using Conversion;
using Garmin;
using Peloton;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebApp.Models;

namespace WebApp.Services
{
	public interface ISyncService 
	{
		Task<SyncPostResponse> SyncAsync(int numWorkouts);
	}

	public class SyncService : ISyncService
	{
		private static readonly ILogger _logger = LogContext.ForClass<SyncService>();

		private readonly IAppConfiguration _config;
		private readonly IPelotonService _pelotonService;
		private readonly IGarminUploader _garminUploader;
		private readonly IEnumerable<IConverter> _converters;
		private readonly IDbClient _db;

		public SyncService(IAppConfiguration config, IPelotonService pelotonService, IGarminUploader garminUploader, IEnumerable<IConverter> converters, IDbClient dbClient)
		{
			_config = config;
			_pelotonService = pelotonService;
			_garminUploader = garminUploader;
			_converters = converters;
			_db = dbClient;
		}

		public async Task<SyncPostResponse> SyncAsync(int numWorkouts)
		{
			_logger.Verbose("Reached the SyncController.");
			var response = new SyncPostResponse();
			var syncTime = _db.GetSyncStatus();

			try
			{
				await _pelotonService.DownloadLatestWorkoutDataAsync(numWorkouts);
				response.PelotonDownloadSuccess = true;

			}
			catch (Exception e)
			{
				_logger.Error(e, "Failed to download workouts from Peleoton.");
				response.SyncSuccess = false;
				response.PelotonDownloadSuccess = false;
				response.Errors.Add(new ErrorResponse() { Message = "Failed to download workouts from Peloton. Check logs for more details." });
				return response;
			}

			try
			{
				foreach (var converter in _converters)
				{
					converter.Convert();
				}
				response.ConverToFitSuccess = true;
			}
			catch (Exception e)
			{
				_logger.Error(e, "Failed to convert workouts to FIT format.");
				response.SyncSuccess = false;
				response.ConverToFitSuccess = false;
				response.Errors.Add(new ErrorResponse() { Message = "Failed to convert workouts to FIT format. Check logs for more details." });
				return response;
			}

			try
			{
				await _garminUploader.UploadToGarminAsync();
				response.UploadToGarminSuccess = true;
			}
			catch (GarminUploadException e)
			{
				_logger.Error(e, "GUpload returned an error code. Failed to upload workouts.");
				_logger.Warning("GUpload failed to upload files. You can find the converted files at {@Path} \n You can manually upload your files to Garmin Connect, or wait for P2G to try again on the next sync job.", _config.App.OutputDirectory);

				response.SyncSuccess = false;
				response.UploadToGarminSuccess = false;
				response.Errors.Add(new ErrorResponse() { Message = "Failed to upload to Garmin Connect. Check logs for more details." });
				return response;
			}

			syncTime.LastSyncTime = DateTime.Now;
			syncTime.LastSuccessfulSyncTime = DateTime.Now;
			_db.UpsertSyncStatus(syncTime);

			response.SyncSuccess = true;
			return response;
		}
	}
}
