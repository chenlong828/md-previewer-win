// WebView2 宿主前导脚本：在内核 bundle 求值前注入（AddScriptToExecuteOnDocumentCreatedAsync）。
// 职责只有两件：把 __mdHostChannel.post 接到 chrome.webview，把壳发来的帧转给 __mdKernel.receive。
// 内核 transport 缺 __mdHostChannel.post 会显式失败，因此这段必须先于 bundle 执行。
(function () {
    if (!window.chrome || !window.chrome.webview) {
        console.error('mdpreview: chrome.webview missing; shell prelude cannot install channel');
        return;
    }
    window.__mdHostChannel = Object.freeze({
        post: function (json) {
            window.chrome.webview.postMessage(json);
        }
    });
    // 内核 bundle 求值前可能已有帧到达（壳侧不等 boot 就会早发）；先暂存，待 receive 可用再补投。
    var pending = [];
    var receiveReady = false;

    function deliver(json) {
        var receive = window.__mdKernel && window.__mdKernel.receive;
        if (typeof receive !== 'function') return false;
        receiveReady = true;
        try {
            receive(json);
        } catch (e) {
            console.error('mdpreview: kernel receive failed: ' + (e && e.message ? e.message : e));
        }
        return true;
    }

    function flush() {
        if (!(window.__mdKernel && window.__mdKernel.receive)) return;
        while (pending.length > 0) {
            var json = pending.shift();
            if (!deliver(json)) {
                pending.unshift(json);
                return;
            }
        }
    }

    window.chrome.webview.addEventListener('message', function (event) {
        if (!receiveReady || pending.length > 0) {
            pending.push(event.data);
            flush();
            return;
        }
        deliver(event.data);
    });

    var poll = setInterval(function () {
        if (!(window.__mdKernel && window.__mdKernel.receive)) return;
        clearInterval(poll);
        flush();
    }, 20);

    // 壳级快捷键：WebView 是原生子窗口，WPF 的 InputBinding 拿不到按键，这里统一转成宿主消息。
    // 只拦壳有语义的组合，编辑键（撤销/复制/粘贴/查找输入）保持浏览器原生行为。
    var shellShortcuts = {
        's': 'save', 'S': 'saveAs', 'o': 'open', 'n': 'new', 'w': 'close',
        'f': 'find', 'g': 'findNext', 'G': 'findPrev', 'p': 'print',
        '/': 'toggleSource', '\\': 'toggleDrawer',
        '+': 'zoomIn', '=': 'zoomIn', '-': 'zoomOut', '0': 'zoomReset'
    };
    window.addEventListener('keydown', function (event) {
        if (!(event.ctrlKey || event.metaKey) || event.altKey) return;
        var name = shellShortcuts[event.key];
        if (!name) return;
        event.preventDefault();
        event.stopPropagation();
        window.chrome.webview.postMessage(JSON.stringify({ __host: 'shortcut', key: name }));
    }, true);
    // 右键点：WebView2 的 ContextMenuRequested 只给 WebView 坐标；页面先按 CSS 像素记录
    // clientX/Y，壳在菜单弹出前用 focusAt 定位（与 ProseMirror posAtCoords 同口径）。
    window.addEventListener('contextmenu', function (event) {
        window.__mdCtxPoint = [event.clientX, event.clientY];
    }, true);
    // 拖入图片文件：Chromium 不会把文件路径交给页面（安全），但读得到字节，
    // 因此这里取 files[] → base64 → 宿主消息，落盘与插入由壳完成（磁盘写入只在壳）。
    function readBase64(file) {
        return new Promise(function (resolve, reject) {
            var reader = new FileReader();
            reader.onload = function () {
                var bytes = new Uint8Array(reader.result);
                var bin = '';
                var chunk = 0x8000;
                for (var i = 0; i < bytes.length; i += chunk) {
                    bin += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
                }
                resolve(btoa(bin));
            };
            reader.onerror = function () { reject(reader.error); };
            reader.readAsArrayBuffer(file);
        });
    }

    function imageFiles(dataTransfer) {
        var files = (dataTransfer && dataTransfer.files) ? Array.prototype.slice.call(dataTransfer.files) : [];
        return files.filter(function (f) { return f && f.type && f.type.indexOf('image/') === 0; });
    }

    window.addEventListener('dragover', function (event) {
        if (!imageFiles(event.dataTransfer).length) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
    }, true);

    window.addEventListener('drop', function (event) {
        if (event.target && event.target.closest && event.target.closest('.md-image')) return; // 图片块自身的编辑态交给页面
        var images = imageFiles(event.dataTransfer);
        if (!images.length) return;
        event.preventDefault();
        event.stopPropagation();
        images.forEach(function (file) {
            readBase64(file).then(function (data) {
                window.chrome.webview.postMessage(JSON.stringify({
                    __host: 'dropImage', mime: file.type || '', name: file.name || '', data: data
                }));
            }).catch(function (e) {
                console.error('mdpreview: read dropped image failed: ' + (e && e.message ? e.message : e));
            });
        });
    }, true);
})();
