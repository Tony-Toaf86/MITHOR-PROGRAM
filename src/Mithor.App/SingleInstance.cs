using System;
using System.Threading;

namespace Mithor.App
{
    /// <summary>
    /// Garantiza una única instancia por sesión y permite que un segundo lanzamiento
    /// (doble clic en el acceso directo) muestre la ventana ya existente.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = "Local\\MITHOR.SingleInstance";
        private const string EventName = "Local\\MITHOR.Activate";

        private Mutex _mutex;
        private EventWaitHandle _event;
        private RegisteredWaitHandle _registration;

        public event EventHandler ActivationRequested;

        public static SingleInstance TryAcquire()
        {
            Mutex mutex;
            bool created;
            try
            {
                mutex = new Mutex(true, MutexName, out created);
            }
            catch (AbandonedMutexException)
            {
                mutex = new Mutex(true, MutexName, out created);
            }
            catch (Exception)
            {
                // Sin exclusividad seguimos funcionando: es mejor abrir dos ventanas
                // que impedir el arranque.
                return new SingleInstance();
            }

            if (!created)
            {
                SignalExisting();
                try { mutex.Close(); }
                catch { }
                return null;
            }

            SingleInstance instance = new SingleInstance();
            instance._mutex = mutex;
            try
            {
                instance._event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                instance._registration = ThreadPool.RegisterWaitForSingleObject(
                    instance._event, instance.OnSignal, null, Timeout.Infinite, false);
            }
            catch
            {
            }
            return instance;
        }

        private static void SignalExisting()
        {
            try
            {
                using (EventWaitHandle ev = EventWaitHandle.OpenExisting(EventName))
                {
                    ev.Set();
                }
            }
            catch
            {
            }
        }

        private void OnSignal(object state, bool timedOut)
        {
            EventHandler handler = ActivationRequested;
            if (handler == null)
            {
                return;
            }
            try { handler(this, EventArgs.Empty); }
            catch { }
        }

        public void Dispose()
        {
            if (_registration != null)
            {
                try { _registration.Unregister(null); }
                catch { }
                _registration = null;
            }
            if (_event != null)
            {
                try { _event.Close(); }
                catch { }
                _event = null;
            }
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); }
                catch { }
                try { _mutex.Close(); }
                catch { }
                _mutex = null;
            }
        }
    }
}
