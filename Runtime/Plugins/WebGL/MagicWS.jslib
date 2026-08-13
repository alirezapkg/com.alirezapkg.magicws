mergeInto(LibraryManager.library, {
    $MagicWSState: {
        ws: null
    },

    MagicWS_Connect: function (urlPtr, onOpenCb, onMessageCb, onCloseCb, onErrorCb) {
        var url = UTF8ToString(urlPtr);
        
        if (MagicWSState.ws) {
            MagicWSState.ws.close();
        }

        MagicWSState.ws = new WebSocket(url);
        MagicWSState.ws.binaryType = "arraybuffer";

        MagicWSState.ws.onopen = function () {
            {{{ makeDynCall('v', 'onOpenCb') }}}();
        };

        MagicWSState.ws.onmessage = function (event) {
            var array = new Uint8Array(event.data);
            var buffer = _malloc(array.length);
            HEAPU8.set(array, buffer);
            
            {{{ makeDynCall('vii', 'onMessageCb') }}}(buffer, array.length);
            
            _free(buffer);
        };

        MagicWSState.ws.onclose = function () {
            {{{ makeDynCall('v', 'onCloseCb') }}}();
        };

        MagicWSState.ws.onerror = function (event) {
            {{{ makeDynCall('v', 'onErrorCb') }}}();
        };
    },

    MagicWS_Send: function (ptr, length) {
        if (MagicWSState.ws && MagicWSState.ws.readyState === 1) {
            var array = HEAPU8.subarray(ptr, ptr + length);
            MagicWSState.ws.send(array.buffer);
        }
    },

    MagicWS_Close: function () {
        if (MagicWSState.ws) {
            MagicWSState.ws.close();
            MagicWSState.ws = null;
        }
    }
});