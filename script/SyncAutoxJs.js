// START  User Config  
const url = 'http://192.168.5.194:5033'                 // no slash(/) at the end of url
const username = 'admin'
const token = 'admin'
const intervalTime = 3 * 1000                           // 3 seconds
const showToastNotification = true
// END    User Config  

const axios = require('axios');

const authHeader = 'basic ' + $base64.encode(`${username}:${token}`)
const apiUrl = url + '/SyncClipboard.json'

let running = false
let remoteCache;

function loop() {
    if (!device.isScreenOn())
        return
    if (running)
        return
    running = true

    upload()
        .then(ifContinue => {
            if (ifContinue)
                download()
        })
        .then(_ => { running = false })
        .catch(error => {
            running = false
            toast('Failed: \n' + error)
        });
}

function download() {
    axios({
        method: 'get',
        url: apiUrl,
        responseType: 'json',
        headers: { 'authorization': authHeader },
    })
        .then(res => {
            if (res.status < 200 || res.status >= 300) {
                throw res.status + ' ' + res.statusText
            } else {
                var profile = res.data;
                if (profile.Type != 'Text')
                    return

                var text = profile.Clipboard
                if (text != remoteCache) {
                    remoteCache = text
                    setClip(text)
                    if(showToastNotification)
                        toast('Cipboard Setted\n' + text)
                }
            }
        })
}

function upload() {
    let text = getClip()
    if (text != remoteCache && text != null && text.length != 0) {
        return axios({
            method: 'put',
            url: apiUrl,
            headers: {
                'authorization': authHeader,
                'Content-Type': 'application/json',
            },
            data: {
                'File': '',
                'Clipboard': text,
                'Type': 'Text'
            }
        }).then(res => {
            if (res.status < 200 || res.status >= 300) {
                throw res.status + ' ' + res.statusText
            }
            remoteCache = text
            return false
        })
    }
    return Promise.resolve(true);
}

setInterval(loop, intervalTime)