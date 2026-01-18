// START  User Config  
const url = 'http://192.168.5.194:5033'
const username = 'admin'
const token = 'admin'
const intervalTime = 3 * 1000                        // 3 seconds
const showToastNotification = true
// END    User Config  

const axios = require('axios');

const authHeader = 'basic ' + $base64.encode(`${username}:${token}`)

let urlWithoutSlash = url   
while (urlWithoutSlash.endsWith('/'))
    urlWithoutSlash = urlWithoutSlash.substring(0, urlWithoutSlash.length - 1)
const apiUrl = urlWithoutSlash + '/SyncClipboard.json'

let running = false
let remoteCache;

function loop() {
    if (!device.isScreenOn()) return;
    if (running) return;
    running = true;

    upload()
        .then(ifContinue => {
            // 如果 upload 返回 true (即没有新内容需要上传)，则尝试 download
            if (ifContinue) {
                return download();
            }
        })
        .finally(() => { 
            running = false; 
        })
        .catch(error => {
            console.error(error);
            toast('Sync Error: \n' + error);
        });
}

function download() {
    return axios({
        method: 'get',
        url: apiUrl,
        responseType: 'json',
        headers: { 'authorization': authHeader },
    })
    .then(res => {
        if (res.status < 200 || res.status >= 300) {
            throw res.status + ' ' + res.statusText;
        } else {
            const profile = res.data;
            
            if (profile.type !== 'Text' || profile.hasData === true) {
                return;
            }

            const text = profile.text;
            if (text && text !== remoteCache) {
                remoteCache = text;
                setClip(text);
                if (showToastNotification) {
                    // 提示时截断过长文字，优化体验
                    let logText = text.length > 20 ? text.substring(0, 20) + "..." : text;
                    toast('同步已更新:\n' + logText);
                }
            }
        }
    });
}

function upload() {
    let text = getClip();
    // 只有当本地剪贴板与缓存不一致且不为空时才上传
    if (text && text !== remoteCache && text.length !== 0) {
        return axios({
            method: 'put',
            url: apiUrl,
            headers: {
                'authorization': authHeader,
                'Content-Type': 'application/json',
            },
            data: {
                "hasData": false,
                "text": text,
                "type": "Text"
            }
        }).then(res => {
            if (res.status < 200 || res.status >= 300) {
                throw res.status + ' ' + res.statusText;
            }
            remoteCache = text;
            return false; // 代表执行了上传，此次循环不再执行下载
        });
    }
    return Promise.resolve(true); // 代表没有上传，可以继续执行下载逻辑
}

setInterval(loop, intervalTime);
