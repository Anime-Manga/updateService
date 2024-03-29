﻿using Cesxhin.AnimeManga.Application.CheckManager.Interfaces;
using Cesxhin.AnimeManga.Modules.Exceptions;
using Cesxhin.AnimeManga.Modules.Generic;
using Cesxhin.AnimeManga.Modules.NlogManager;
using Cesxhin.AnimeManga.Modules.Parallel;
using Cesxhin.AnimeManga.Domain.DTO;
using MassTransit;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace Cesxhin.AnimeManga.Application.CheckManager
{
    public class UpdateVideo : IUpdate
    {
        //interface
        private readonly IBus _publishEndpoint;

        //log
        private readonly NLogConsole _logger = new(LogManager.GetCurrentClassLogger());

        //env
        private readonly string _folder = Environment.GetEnvironmentVariable("BASE_PATH") ?? "/";
        private readonly JObject schemas = JObject.Parse(Environment.GetEnvironmentVariable("SCHEMA"));

        //Instance Parallel
        private readonly ParallelManager<object> parallel = new();

        //Istance Api
        private readonly Api<GenericVideoDTO> videoApi = new();
        private readonly Api<EpisodeDTO> episodeApi = new();
        private readonly Api<EpisodeRegisterDTO> episodeRegisterApi = new();

        //download api
        private List<GenericVideoDTO> listVideo = null;

        public UpdateVideo(IBus publicEndpoint)
        {
            _publishEndpoint = publicEndpoint;
        }

        public void ExecuteUpdate()
        {
            _logger.Info($"Start update video");
            foreach (var item in schemas)
            {
                var schema = schemas.GetValue(item.Key).ToObject<JObject>();
                if (schema.GetValue("type").ToString() == "video")
                {
                    try
                    {
                        var query = new Dictionary<string, string>()
                        {
                            ["nameCfg"] = item.Key
                        };

                        listVideo = videoApi.GetMore("/video/all", query).GetAwaiter().GetResult();
                    }
                    catch (ApiNotFoundException ex)
                    {
                        _logger.Error($"Not found get all, details error: {ex.Message}");
                    }
                    catch (ApiGenericException ex)
                    {
                        _logger.Fatal($"Error generic get all, details error: {ex.Message}");
                    }

                    //if exists listAnime
                    if (listVideo != null)
                    {
                        var tasks = new List<Func<object>>();
                        //step one check file
                        foreach (var video in listVideo)
                        {
                            //foreach episodes
                            foreach (var episode in video.Episodes)
                            {
                                tasks.Add(new Func<object>(() => CheckEpisode(video, episode, episodeApi, episodeRegisterApi)));
                            }
                        }
                        parallel.AddTasks(tasks);
                        parallel.Start();
                        parallel.WhenCompleted();
                        parallel.ClearList();
                    }
                }
            }
            _logger.Info($"End update anime");
        }

        private object CheckEpisode(GenericVideoDTO video, EpisodeDTO episode, Api<EpisodeDTO> episodeApi, Api<EpisodeRegisterDTO> episodeRegisterApi)
        {
            var episodeRegister = video.EpisodesRegister.Find(e => e.EpisodeId == episode.ID);
            if (episodeRegister == null)
            {
                _logger.Warn($"not found episodeRegister by episode id: {episode.ID}");
                return null;
            }

            _logger.Debug($"check {episodeRegister.EpisodePath}");

            //check integry file
            if (episode.StateDownload == null || episode.StateDownload == "failed" || (episode.StateDownload == "completed" && episodeRegister.EpisodeHash == null))
            {
                ConfirmStartDownload(episode, episodeApi, CalculatePriority(video, episode));
            }
            else if (!File.Exists(episodeRegister.EpisodePath) && episode.StateDownload == "completed")
            {
                var found = false;
                string newHash;
                foreach (string file in Directory.EnumerateFiles(_folder, "*.mp4", SearchOption.AllDirectories))
                {
                    newHash = Hash.GetHash(file);
                    if (newHash == episodeRegister.EpisodeHash)
                    {
                        _logger.Info($"I found file (episode id: {episode.ID}) that was move, now update information");

                        //update
                        episodeRegister.EpisodePath = file;
                        try
                        {
                            episodeRegisterApi.PutOne("/episode/register", episodeRegister).GetAwaiter().GetResult();

                            _logger.Info($"Ok update episode id: {episode.ID} that was move");

                            //return
                            found = true;
                        }
                        catch (ApiNotFoundException ex)
                        {
                            _logger.Error($"Not found episodeRegister id: {episodeRegister.EpisodeId} for update information, details: {ex.Message}");
                        }
                        catch (ApiConflictException ex)
                        {
                            _logger.Error($"Error conflict put episodeRegister, details error: {ex.Message}");
                        }
                        catch (ApiGenericException ex)
                        {
                            _logger.Fatal($"Error generic put episodeRegister, details error: {ex.Message}");
                        }

                        break;
                    }
                }

                //if not found file
                if (found == false)
                    ConfirmStartDownload(episode, episodeApi, CalculatePriority(video, episode));
            }

            return null;
        }

        private async void ConfirmStartDownload(EpisodeDTO episode, Api<EpisodeDTO> episodeApi, int priority)
        {
            //set pending to 
            episode.StateDownload = "pending";

            try
            {
                //set change status
                await episodeApi.PutOne("/video/statusDownload", episode);

                await _publishEndpoint.Publish(episode, (context) => context.SetPriority((byte)priority));
                _logger.Info($"this file ({episode.VideoId} episode: {episode.NumberEpisodeCurrent}) does not exists, sending message to DownloadService");
            }
            catch (ApiNotFoundException ex)
            {
                _logger.Error($"Impossible update episode becouse not found episode id: {episode.ID}, details: {ex.Message}");
            }
            catch (ApiGenericException ex)
            {
                _logger.Fatal($"Error update episode, details error: {ex.Message}");
            }
        }

        private int CalculatePriority(GenericVideoDTO video, EpisodeDTO episode)
        {
            return 255 - (episode.NumberEpisodeCurrent * 255 / (video.Episodes.Count + 1));
        }
    }
}
