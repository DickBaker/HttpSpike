﻿using System;
using System.Threading.Tasks;
using Infrastructure.Models;

namespace Infrastructure.Interfaces
{
    public interface IDownloader
    {
        Task<bool> FetchFileAsync(WebPage webpage, IProgress<int> progress = null);
    }
}