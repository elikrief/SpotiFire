﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.InteropServices;

namespace SpotiFire.SpotifyLib
{
    public delegate void PlaylistContainerHandler(IPlaylistContainer pc, EventArgs args);
    public delegate void PlaylistContainerHandler<TEventArgs>(IPlaylistContainer pc, TEventArgs args) where TEventArgs : EventArgs;
    internal class PlaylistContainer : CountedDisposeableSpotifyObject, IPlaylistContainer
    {
        #region Wrapper
        private class PlaylistContainerWrapper : DisposeableSpotifyObject, IPlaylistContainer
        {
            internal PlaylistContainer pc;
            public PlaylistContainerWrapper(PlaylistContainer pc)
            {
                this.pc = pc;
                pc.Loaded += new PlaylistContainerHandler(pc_Loaded);
                pc.PlaylistAdded += new PlaylistContainerHandler<PlaylistEventArgs>(pc_PlaylistAdded);
                pc.PlaylistMoved += new PlaylistContainerHandler<PlaylistMovedEventArgs>(pc_PlaylistMoved);
                pc.PlaylistRemoved += new PlaylistContainerHandler<PlaylistEventArgs>(pc_PlaylistRemoved);
            }

            void pc_PlaylistRemoved(IPlaylistContainer pc, PlaylistEventArgs args)
            {
                if (PlaylistRemoved != null)
                    PlaylistRemoved(this, args);
            }

            void pc_PlaylistMoved(IPlaylistContainer pc, PlaylistMovedEventArgs args)
            {
                if (PlaylistMoved != null)
                    PlaylistMoved(this, null);
            }

            void pc_PlaylistAdded(IPlaylistContainer pc, PlaylistEventArgs args)
            {
                if (PlaylistAdded != null)
                    PlaylistAdded(this, args);
            }

            void pc_Loaded(IPlaylistContainer pc, EventArgs args)
            {
                if (Loaded != null)
                    Loaded(this, args);
            }

            protected override void OnDispose()
            {
                pc.Loaded -= new PlaylistContainerHandler(pc_Loaded);
                pc.PlaylistAdded -= new PlaylistContainerHandler<PlaylistEventArgs>(pc_PlaylistAdded);
                pc.PlaylistMoved -= new PlaylistContainerHandler<PlaylistMovedEventArgs>(pc_PlaylistMoved);
                pc.PlaylistRemoved -= new PlaylistContainerHandler<PlaylistEventArgs>(pc_PlaylistRemoved);
                PlaylistContainer.Delete(pc.pcPtr);
                pc = null;
            }

            protected override int IntPtrHashCode()
            {
                return IsAlive() ? pc.IntPtrHashCode() : 0;
            }

            public event PlaylistContainerHandler Loaded;

            public event PlaylistContainerHandler<PlaylistEventArgs> PlaylistAdded;

            public event PlaylistContainerHandler<PlaylistMovedEventArgs> PlaylistMoved;

            public event PlaylistContainerHandler<PlaylistEventArgs> PlaylistRemoved;

            public Session Session
            {
                get { IsAlive(true); return pc.Session; }
            }

            public IEditableArray<IPlaylist> Playlists
            {
                get { IsAlive(true); return pc.Playlists; }
            }
        }

        internal static IntPtr GetPointer(IPlaylistContainer pc)
        {
            if (pc.GetType() == typeof(PlaylistContainerWrapper))
                return ((PlaylistContainerWrapper)pc).pc.pcPtr;
            throw new ArgumentException("Invalid pc");
        }
        #endregion
        #region Counter
        private static Dictionary<IntPtr, PlaylistContainer> pcs = new Dictionary<IntPtr, PlaylistContainer>();
        private static readonly object pcsLock = new object();

        internal static IPlaylistContainer Get(Session session, IntPtr pcPtr)
        {
            PlaylistContainer pc;
            lock (pcsLock)
            {
                if (!pcs.ContainsKey(pcPtr))
                {
                    pcs.Add(pcPtr, new PlaylistContainer(session, pcPtr));
                }
                pc = pcs[pcPtr];
                pc.AddRef();
            }
            return new PlaylistContainerWrapper(pc);
        }

        internal static void Delete(IntPtr pcPtr)
        {
            lock (pcsLock)
            {
                PlaylistContainer pc = pcs[pcPtr];
                int count = pc.RemRef();
                if (count == 0)
                    pcs.Remove(pcPtr);
            }
        }
        #endregion

        #region Structs
        private struct sp_playlistcontainer_callbacks
        {
            internal IntPtr playlist_added;
            internal IntPtr playlist_removed;
            internal IntPtr playlist_moved;
            internal IntPtr container_loaded;
        }
        #endregion

        #region Delegates
        private delegate void playlist_added_cb(IntPtr pcPtr, IntPtr playlistPtr, int position, IntPtr userdataPtr);
        private delegate void playlist_removed_cb(IntPtr pcPtr, IntPtr playlistPtr, int position, IntPtr userdataPtr);
        private delegate void playlist_moved_cb(IntPtr pcPtr, IntPtr playlistPtr, int position, int newPosition, IntPtr userdataPtr);
        private delegate void container_loaded_cb(IntPtr pcPtr, IntPtr userdataPtr);
        #endregion

        #region Declarations
        IntPtr pcPtr = IntPtr.Zero;
        private Session session;

        private sp_playlistcontainer_callbacks callbacks;
        private IntPtr callbacksPtr;
        private playlist_added_cb playlist_added;
        private playlist_removed_cb playlist_removed;
        private playlist_moved_cb playlist_moved;
        private container_loaded_cb container_loaded;

        private IEditableArray<IPlaylist> playlists;
        #endregion

        #region Constructor
        private PlaylistContainer(Session session, IntPtr pcPtr)
        {
            if (pcPtr == IntPtr.Zero)
                throw new ArgumentException("pcPtr can't be zero");

            if (session == null)
                throw new ArgumentNullException("Session can't be null");

            this.session = session;
            this.pcPtr = pcPtr;

            this.playlist_added = new playlist_added_cb(PlaylistAddedCallback);
            this.playlist_removed = new playlist_removed_cb(PlaylistRemovedCallback);
            this.playlist_moved = new playlist_moved_cb(PlaylistMovedCallback);
            this.container_loaded = new container_loaded_cb(ContainerLoadedCallback);
            this.callbacks = new sp_playlistcontainer_callbacks
            {
                playlist_added = Marshal.GetFunctionPointerForDelegate(playlist_added),
                playlist_removed = Marshal.GetFunctionPointerForDelegate(playlist_removed),
                playlist_moved = Marshal.GetFunctionPointerForDelegate(playlist_moved),
                container_loaded = Marshal.GetFunctionPointerForDelegate(container_loaded)
            };

            int size = Marshal.SizeOf(this.callbacks);
            this.callbacksPtr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(this.callbacks, this.callbacksPtr, true);

            lock (libspotify.Mutex)
            {
                libspotify.sp_playlistcontainer_add_callbacks(pcPtr, callbacksPtr, IntPtr.Zero);
            }

            session.DisposeAll += new SessionEventHandler(session_DisposeAll);

            playlists = new DelegateList<IPlaylist>(
                () =>
                {
                    IsAlive(true);
                    lock (libspotify.Mutex)
                        return libspotify.sp_playlistcontainer_num_playlists(pcPtr);
                },
                (index) =>
                {
                    IsAlive(true);
                    lock (libspotify.Mutex)
                        return Playlist.Get(session, this, libspotify.sp_playlistcontainer_playlist(pcPtr, index), libspotify.sp_playlistcontainer_playlist_type(pcPtr, index));
                },
                (playlist) =>
                {
                    IsAlive(true);
                    lock (libspotify.Mutex)
                        libspotify.sp_playlistcontainer_add_new_playlist(pcPtr, playlist.Name);
                },
                (index) =>
                {
                    IsAlive(true);
                    lock (libspotify.Mutex)
                        libspotify.sp_playlistcontainer_remove_playlist(pcPtr, index);
                },
                () => false
            );
        }

        void session_DisposeAll(Session sender, SessionEventArgs e)
        {
            Dispose();
        }
        #endregion

        #region Properties
        public IEditableArray<IPlaylist> Playlists
        {
            get { return playlists; }
        }
        public Session Session
        {
            get
            {
                IsAlive(true);
                return session;
            }
        }
        #endregion

        #region Private Methods
        private static Delegate CreateDelegate<T>(Expression<Func<PlaylistContainer, Action<T>>> expr, PlaylistContainer s) where T : EventArgs
        {
            return expr.Compile().Invoke(s);
        }
        private void PlaylistAddedCallback(IntPtr pcPtr, IntPtr playlistPtr, int position, IntPtr userdataPtr)
        {
            if (pcPtr == this.pcPtr)
                session.EnqueueEventWorkItem(new EventWorkItem(CreateDelegate<PlaylistEventArgs>(pc => pc.OnPlaylistAdded, this), new PlaylistEventArgs(playlistPtr, position)));
        }
        private void PlaylistRemovedCallback(IntPtr pcPtr, IntPtr playlistPtr, int position, IntPtr userdataPtr)
        {
            if (pcPtr == this.pcPtr)
                session.EnqueueEventWorkItem(new EventWorkItem(CreateDelegate<PlaylistEventArgs>(pc => pc.OnPlaylistRemoved, this), new PlaylistEventArgs(playlistPtr, position)));
        }
        private void PlaylistMovedCallback(IntPtr pcPtr, IntPtr playlistPtr, int position, int newPosition, IntPtr userdataPtr)
        {
            if (pcPtr == this.pcPtr)
                session.EnqueueEventWorkItem(new EventWorkItem(CreateDelegate<PlaylistMovedEventArgs>(pc => pc.OnPlaylistMoved, this), new PlaylistMovedEventArgs(playlistPtr, position, newPosition)));
        }
        private void ContainerLoadedCallback(IntPtr pcPtr, IntPtr userdataPtr)
        {
            if (pcPtr == this.pcPtr)
                session.EnqueueEventWorkItem(new EventWorkItem(CreateDelegate<EventArgs>(pc => pc.OnLoaded, this), new EventArgs()));
        }
        #endregion

        #region Protected Methods
        protected virtual void OnPlaylistAdded(PlaylistEventArgs args)
        {
            if (PlaylistAdded != null)
                PlaylistAdded(this, args);
        }
        protected virtual void OnPlaylistRemoved(PlaylistEventArgs args)
        {
            if (PlaylistRemoved != null)
                PlaylistRemoved(this, args);
        }
        protected virtual void OnPlaylistMoved(PlaylistMovedEventArgs args)
        {
            if (PlaylistMoved != null)
                PlaylistMoved(this, args);
        }
        protected virtual void OnLoaded(EventArgs args)
        {
            if (Loaded != null)
                Loaded(this, args);
        }
        #endregion

        #region Internal Playlist methods
        internal sp_playlist_type GetPlaylistType(Playlist playlist)
        {
            int index = playlists.IndexOf(playlist);
            lock (libspotify.Mutex)
                return libspotify.sp_playlistcontainer_playlist_type(pcPtr, index);
        }
        #endregion

        #region Events
        public event PlaylistContainerHandler<PlaylistEventArgs> PlaylistAdded;
        public event PlaylistContainerHandler<PlaylistEventArgs> PlaylistRemoved;
        public event PlaylistContainerHandler<PlaylistMovedEventArgs> PlaylistMoved;
        public event PlaylistContainerHandler Loaded;
        #endregion

        #region Cleanup
        protected override void OnDispose()
        {
            session.DisposeAll -= new SessionEventHandler(session_DisposeAll);

            if (!session.ProcExit)
                lock (libspotify.Mutex)
                {
                    try { libspotify.sp_playlistcontainer_remove_callbacks(pcPtr, callbacksPtr, IntPtr.Zero); }
                    catch { }
                }
            try { Marshal.FreeHGlobal(callbacksPtr); }
            catch { }
            callbacksPtr = IntPtr.Zero;
            pcPtr = IntPtr.Zero;
        }
        #endregion

        protected override int IntPtrHashCode()
        {
            return pcPtr.GetHashCode();
        }
    }
}
