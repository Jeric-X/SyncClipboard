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
    urlWithoutSlash = urlWithoutSlash.substring(0, urlWithoutSlash.length - 1)
const apiUrl = urlWithoutSlash + '/SyncClipboard.json'

/**
 * 核心修改部分：适配新版 JSON 结构
 */
function upload(verifyCode) {
    if (verifyCode !== null && verifyCode.length !== 0) {
        return axios({
            method: 'put',
            url: apiUrl,
            headers: {
                'authorization': authHeader,
                'Content-Type': 'application/json',
            },
            data: {
                // 变更点 1: 新版使用 hasData 标识是否有文件，验证码文本设为 false
                'hasData': false,
                // 变更点 2: 字段名由 Clipboard 变更为 text
                'text': verifyCode,
                // 变更点 3: 字段名由 Type 变更为小写的 type
                'type': 'Text'
            }
        }).then(res => {
            if (res.status < 200 || res.status >= 300) {
                throw res.status + ' ' + res.statusText
            }
        }).then(() => {
            if (showToastNotification) {
                toast('验证码已上传: ' + verifyCode)
            }
        }).catch(error => {
            console.error(error);
            if (showToastNotification) {
                toast('验证码上传失败: \n' + error)
            }
        })
    }
}

// 监听系统通知
events.observeNotification();
events.onNotification(notification => {
    const content = notification.getText();
    if (content !== null && (content.includes('验证') || content.includes('码'))) {
        // 正则匹配 4 位及以上的数字
        var res = /\d{4,}/.exec(content)
        if (res !== null) {
            upload(res[0])
        }
    }
})
