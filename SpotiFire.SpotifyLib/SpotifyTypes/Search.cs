﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using SPSearch = SpotiFire.Search;

namespace SpotiFire
{
    public delegate void SearchEventHandler(ISearch sender, SearchEventArgs e);
}

namespace SpotiFire.Types
{
    internal class Search : CountedDisposeableSpotifyObject, ISearch, ISpotifyAwaitable
    {
        #region Wrapper
        private sealed class SearchWrapper : DisposeableSpotifyObject, ISearch, ISpotifyAwaitable
        {
            internal Search search;
            public SearchWrapper(Search search)
            {
                this.search = search;
                search.Complete += new SearchEventHandler(search_Complete);
            }

            void search_Complete(ISearch sender, SearchEventArgs e)
            {
                if (sender == search)
                    if (Complete != null)
                        Complete(this, e);
            }

            protected override void OnDispose()
            {
                search.Complete -= new SearchEventHandler(search_Complete);
                Search.Delete(search.searchPtr);
                search = null;
            }

            public IArray<IAlbum> Albums
            {
                get { IsAlive(true); return search.Albums; }
            }

            public IArray<IArtist> Artists
            {
                get { IsAlive(true); return search.Artists; }
            }

            public event SearchEventHandler Complete;

            public string DidYouMean
            {
                get { IsAlive(true); return search.DidYouMean; }
            }

            public Error Error
            {
                get { IsAlive(true); return search.Error; }
            }

            public string Query
            {
                get { IsAlive(true); return search.Query; }
            }

            public int TotalAlbums
            {
                get { IsAlive(true); return search.TotalAlbums; }
            }

            public int TotalArtists
            {
                get { IsAlive(true); return search.TotalArtists; }
            }

            public int TotalTracks
            {
                get { IsAlive(true); return search.TotalTracks; }
            }

            public IArray<ITrack> Tracks
            {
                get { IsAlive(true); return search.Tracks; }
            }

            public ISession Session
            {
                get { IsAlive(true); return search.Session; }
            }

            public bool IsComplete
            {
                get { IsAlive(true); return search.IsComplete; }
            }

            protected override int IntPtrHashCode()
            {
                return IsAlive() ? search.searchPtr.GetHashCode() : 0;
            }

            bool ISpotifyAwaitable.IsComplete
            {
                get { IsAlive(true);  return ((ISpotifyAwaitable)search).IsComplete; }
            }

            bool ISpotifyAwaitable.AddContinuation(AwaitHelper.Continuation continuation, bool addBeforeOthers)
            {
                IsAlive(true); return ((ISpotifyAwaitable)search).AddContinuation(continuation, addBeforeOthers);
            }
        }

        internal static IntPtr GetPointer(ISearch search)
        {
            if (search.GetType() == typeof(SearchWrapper))
                return ((SearchWrapper)search).search.searchPtr;
            throw new ArgumentException("Invalid search");
        }
        #endregion
        #region Counter
        private static Dictionary<IntPtr, Search> searchs = new Dictionary<IntPtr, Search>();
        private static readonly object searchsLock = new object();

        internal static ISearch Get(Session session, IntPtr searchPtr)
        {
            Search search;
            lock (searchsLock)
            {
                if (!searchs.ContainsKey(searchPtr))
                {
                    searchs.Add(searchPtr, new Search(session, searchPtr));
                }
                search = searchs[searchPtr];
                search.AddRef();
            }
            return new SearchWrapper(search);
        }

        internal static void Delete(IntPtr searchPtr)
        {
            lock (searchsLock)
            {
                Search search = searchs[searchPtr];
                int count = search.RemRef();
                if (count == 0)
                    searchs.Remove(searchPtr);
            }
        }
        #endregion

        #region Delegates
        internal delegate void search_complete_cb(IntPtr searchPtr, IntPtr userdataPtr);
        #endregion

        #region Declarations

        internal IntPtr searchPtr = IntPtr.Zero;
        private Session session;
        private bool isComplete = false;

        internal static search_complete_cb search_complete = new search_complete_cb(_SearchCompleteCallback);

        private DelegateArray<ITrack> tracks;
        private DelegateArray<IAlbum> albums;
        private DelegateArray<IArtist> artists;
        #endregion

        #region Constructor and setup
        private Search(Session session, IntPtr searchPtr)
        {
            if (searchPtr == IntPtr.Zero)
                throw new ArgumentException("searchPtr can't be zero");

            if (session == null)
                throw new ArgumentNullException("Session can't be null");
            this.session = session;

            this.searchPtr = searchPtr;
            
            this.tracks = new DelegateArray<ITrack>(() =>
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return SPSearch.num_tracks(searchPtr);
            }, (index) =>
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return Track.Get(session, SPSearch.track(searchPtr, index));
            });

            this.albums = new DelegateArray<IAlbum>(() =>
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return SPSearch.num_albums(searchPtr);
            }, (index) =>
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return Album.Get(session, SPSearch.album(searchPtr, index));
            });

            this.artists = new DelegateArray<IArtist>(() =>
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return SPSearch.num_artists(searchPtr);
            }, (index) =>
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return Artist.Get(session, SPSearch.artist(searchPtr, index));
            });

            _Complete += new search_complete_cb(Search__Complete);
            session.DisposeAll += new SessionEventHandler(session_DisposeAll);
        }

        private void Search__Complete(IntPtr searchPtr, IntPtr userdataPtr)
        {
            if (searchPtr == this.searchPtr)
            {
                lock(this)
                    this.isComplete = true;
                session.EnqueueEventWorkItem(new EventWorkItem(CreateDelegate<SearchEventArgs>(s => s.OnComplete, this), new SearchEventArgs(this)));
                _Complete -= new search_complete_cb(Search__Complete);
            }
        }

        static void _SearchCompleteCallback(IntPtr searchPtr, IntPtr userdataPtr)
        {
            if (_Complete != null)
                _Complete(searchPtr, userdataPtr);
        }

        #endregion

        #region Properties
        public ISession Session { get { IsAlive(true); return session; } }

        public Error Error
        {
            get
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return (Error)SPSearch.error(searchPtr);
            }
        }

        public IArray<ITrack> Tracks
        {
            get
            {
                IsAlive(true);
                return tracks;
            }
        }

        public IArray<IAlbum> Albums
        {
            get
            {
                IsAlive(true);
                return albums;
            }
        }

        public IArray<IArtist> Artists
        {
            get
            {
                IsAlive(true);
                return artists;
            }
        }

        public string Query
        {
            get
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return SPSearch.query(searchPtr);
            }
        }

        public string DidYouMean
        {
            get
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return SPSearch.did_you_mean(searchPtr);
            }
        }

        public int TotalTracks
        {
            get
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return SPSearch.total_tracks(searchPtr);
            }
        }

        public int TotalAlbums
        {
            get
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return SPSearch.total_albums(searchPtr);
            }
        }

        public int TotalArtists
        {
            get
            {
                IsAlive(true);
                lock (libspotify.Mutex)
                    return SPSearch.total_artists(searchPtr);
            }
        }

        public bool IsComplete
        {
            get
            {
                IsAlive(true);
                return isComplete;
            }
        }
        #endregion

        #region Public Methods

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[Search]");
            sb.AppendLine("Error=" + Error);
            sb.AppendLine("Tracks.Count=" + Tracks.Count);
            sb.AppendLine("Albums.Count=" + Albums.Count);
            //sb.AppendLine("Artists.Count=" + Artists.Count);
            sb.AppendLine("Query=" + Query);
            sb.AppendLine("DidYouMean=" + DidYouMean);
            sb.AppendLine("TotalTracks=" + TotalTracks);

            return sb.ToString();
        }
        #endregion

        #region Cleanup

        protected override void OnDispose()
        {
            _Complete -= new search_complete_cb(Search__Complete);
            session.DisposeAll -= new SessionEventHandler(session_DisposeAll);

            if (!session.ProcExit)
                lock (libspotify.Mutex)
                    SPSearch.release(searchPtr);

            searchPtr = IntPtr.Zero;
        }

        private void session_DisposeAll(ISession sender, SessionEventArgs e)
        {
            Dispose();
        }
        #endregion

        #region Private Methods
        private static Delegate CreateDelegate<T>(Expression<Func<Search, Action<T>>> expr, Search s) where T : SearchEventArgs
        {
            return expr.Compile().Invoke(s);
        }
        private void ImageLoadedCallback(IntPtr searchPtr, IntPtr userdataPtr)
        {
            if (searchPtr == this.searchPtr)
                session.EnqueueEventWorkItem(new EventWorkItem(CreateDelegate<SearchEventArgs>(s => s.OnComplete, this), new SearchEventArgs(this)));
        }
        #endregion

        #region Protected Methods
        protected virtual void OnComplete(SearchEventArgs args)
        {
            if (this.Complete != null)
                this.Complete(this, args);
        }
        #endregion

        #region Event
        public event SearchEventHandler Complete;
        private static event search_complete_cb _Complete;
        #endregion

        #region Await
        SearchEventHandler _handler;
        List<AwaitHelper.Continuation> _handlers;
        private void EnsureHandler()
        {
            if (_handlers == null)
            {
                _handlers = new List<AwaitHelper.Continuation>();
                _handler = (s, e) =>
                {
                    lock (this)
                    {
                        foreach (var h in _handlers)
                            h.Run(this, true);
                        _handlers = null;
                        Complete -= _handler;
                        _handler = null;
                    }
                };
                Complete += _handler;
            }
        }

        bool ISpotifyAwaitable.AddContinuation(AwaitHelper.Continuation continuation, bool addBeforeOthers)
        {
            lock (this)
            {
                if (IsComplete)
                {
                    return false;
                }
                else
                {
                    EnsureHandler();
                    if (addBeforeOthers)
                        _handlers.Insert(0, continuation);
                    else
                        _handlers.Add(continuation);
                    return true;
                }
            }
        }

        bool ISpotifyAwaitable.IsComplete
        {
            get
            {
                lock(this)
                    return IsComplete;
            }
        }
        #endregion

        protected override int IntPtrHashCode()
        {
            return searchPtr.GetHashCode();
        }
    }
}
