// START  User Config  
const url = 'http://192.168.5.194:5033'
const username = 'admin'
const token = 'admin'
const showToastNotification = true
// END    User Config  

const axios = require('axios');

const authHeader = 'basic ' + $base64.encode(`${username}:${token}`)

let urlWithoutSlash = url
while (urlWithoutSlash.endsWith('/'))
    urlWithoutSlash = url.substring(0, url.length - 1)
const apiUrl = urlWithoutSlash + '/SyncClipboard.json'

function upload(text) {
    if (text != null && text.length != 0) {
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
        }).then(() => {
            if (showToastNotification) {
                toast('验证码已上传')
            }
        }).catch(error => {
            if (showToastNotification) {
                toast('验证码上传失败: \n' + error)
            }
        })
    }
}

events.observeNotification();
events.onNotification(notification => {
    if (notification.getText().includes('验证')) {
        var res = /\d{4,}/.exec(notification.getText())
        if (res != null) {
            upload(res[0])
        }
    }
})