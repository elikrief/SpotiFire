﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpotiFire.SpotifyLib
{
    interface ISpotifyAwaitable
    {
        bool IsComplete { get; }
        void OnCompleted(Action action);
    }
}