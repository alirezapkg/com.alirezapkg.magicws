mergeInto(LibraryManager.library, {
    $MagicWSState: {
        nextId: 0,
        sockets: {}
    },

    MagicWS_Connect: function (urlPtr, onOpenCb, onMessageCb, onCloseCb, onErrorCb) {
        var url = UTF8ToString(urlPtr);
        var id = ++MagicWSState.nextId;
        var ws = new WebSocket(url);
        ws.binaryType = "arraybuffer";
        MagicWSState.sockets[id] = ws;

        ws.onopen = function () {
            {{{ makeDynCall('vi', 'onOpenCb') }}}(id);
        };

        ws.onmessage = function (event) {
            var array = new Uint8Array(event.data);
            var buffer = _malloc(array.length);
            HEAPU8.set(array, buffer);
            
            {{{ makeDynCall('vii', 'onMessageCb') }}}(id, buffer, array.length);
            
            _free(buffer);
        };

        ws.onclose = function () {
            {{{ makeDynCall('vi', 'onCloseCb') }}}(id);
        };

        ws.onerror = function (event) {
            {{{ makeDynCall('vi', 'onErrorCb') }}}(id);
        };

        return id;
    },

    MagicWS_Send: function (id, ptr, length) {
        var ws = MagicWSState.sockets[id];
        if (ws && ws.readyState === 1) {
            var array = HEAPU8.subarray(ptr, ptr + length);
            ws.send(array.buffer);
        }
    },

    MagicWS_Close: function (id) {
        var ws = MagicWSState.sockets[id];
        if (ws) {
            ws.close();
            delete MagicWSState.sockets[id];
        }
    },

    MagicWS_CloseWithCode: function (id, code, reasonPtr) {
        var ws = MagicWSState.sockets[id];
        if (ws) {
            var reason = UTF8ToString(reasonPtr);
            ws.close(code, reason);
            delete MagicWSState.sockets[id];
        }
    }
});