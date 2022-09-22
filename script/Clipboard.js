console.clear()
const user = ""
const token = ""
const authHeader = "basic " + $text.base64Encode(`${user}:${token}`)
const path = ""
let httpPara = {
    url: `https://${path}/SyncClipboard.json`,
    header: {
        authorization: authHeader,
    }
}

$ui.render({
    props: {
        bgcolor: $color("white"),
        title: "SyncClipboard"
    },
    views: [
        {
            type: "view",
            props: {
                // bgcolor: $color("red"),
                // frame: $rect(0, 0, 0, 30),
                // flex: "W",
                id: "view1"
            },
            layout({ top, height }) {
                top.left.right.inset(10)
                height.equalTo(22)
            },
            views: [
                {
                    type: "label",
                    props: {
                        id: "id远程",
                        text: "远程: ",
                    },
                    layout: $align.left
                },
                {
                    type: "label",
                    props: {
                        text: "",
                        id: "remote",
                        flex: "L"
                    },
                    layout({ left, top }) {
                        top.equalTo($("id远程").top);
                        left.equalTo($("id远程").right).offset(10);
                    },
                },
                {
                    type: "spinner",
                    props: {
                        loading: false,
                        id: "下载spinnerId"
                    },
                    layout({ right, top }) {
                        top.equalTo($("id远程").top);
                        right.equalTo(0)
                    },
                },
            ]
        },
        {
            type: "view",
            props: {
                id: "view2",
                // bgcolor: $color("red"),
                // flex: "W",
            },
            layout({ top, height }) {
                top.equalTo($("view1").bottom)
                height.equalTo(34)
                top.left.right.inset(10)
            },
            views: [
                {
                    type: "button",
                    props: {
                        title: "插入",
                        id: "插入Id",
                    },
                    layout: function (make, view) {
                        make.width.equalTo(130)
                        make.left.equalTo(0)
                    }
                },
                {
                    type: "button",
                    props: {
                        title: "插入并返回",
                        id: "插入并返回Id",
                    },
                    layout: function (make, view) {
                        make.left.equalTo($("插入Id").right).offset(10)
                        make.width.equalTo(130)
                    },
                },
                {
                    type: "button",
                    props: {
                        // title: "刷新",
                        id: "freshRemoteId",
                        icon: $icon("162"),
                    },
                    layout: function (make, view) {
                        make.left.equalTo($("插入并返回Id").right).offset(10)
                        make.right.equalTo(0)
                        make.height.equalTo($("插入Id").height)
                    },
                }
            ]
        },
        {
            type: "view",
            props: {
                id: "view3",
                // bgcolor: $color("red")
            },
            layout({ top, height }) {
                top.equalTo($("view2").bottom)
                height.equalTo(22)
                top.left.right.inset(10)
            },
            views: [
                {
                    type: "label",
                    props: {
                        id: "id本地",
                        text: "本地: "
                    },
                    layout: $align.left
                },
                {
                    type: "label",
                    props: {
                        text: "...",
                        id: "本地labelId"
                    },
                    layout({ left, top }) {
                        top.equalTo($("id本地").top);
                        left.equalTo($("id本地").right).offset(10);
                    },
                },
                {
                    type: "spinner",
                    props: {
                        loading: false,
                        id: "上传本地spinnerId"
                    },
                    layout({ right, top }) {
                        top.equalTo($("id本地").top);
                        right.equalTo(0)
                    },
                },
            ]
        },
        {
            type: "view",
            props: {
                id: "view4",
                // bgcolor: $color("red"),
            },
            layout({ top, height }) {
                top.equalTo($("view3").bottom)
                height.equalTo(22)
                top.left.right.inset(10)
            },
            views: [
                {
                    type: "label",
                    props: {
                        id: "id已选",
                        text: "已选: "
                    },
                    layout: $align.left
                },
                {
                    type: "label",
                    props: {
                        text: "...",
                        id: "已选textId"
                    },
                    layout({ left, top }) {
                        top.equalTo($("id已选").top);
                        left.equalTo($("id已选").right).offset(10);
                    },
                },
                {
                    type: "spinner",
                    props: {
                        loading: false,
                        id: "上传已选spinnerId"
                    },
                    layout({ right, top }) {
                        top.equalTo($("id已选").top);
                        right.equalTo(0)
                    },
                },
            ]
        },
        {
            type: "view",
            props: {
                id: "view5",
                // bgcolor: $color("red"),
            },
            layout({ top, height }) {
                top.equalTo($("view4").bottom)
                height.equalTo(34)
                top.left.right.inset(10)
            },
            views: [
                {
                    type: "button",
                    props: {
                        title: "上传本地",
                        id: "上传本地Id",
                    },
                    layout: function (make, view) {
                        make.width.equalTo(130)
                        make.left.equalTo(0)
                    }
                },
                {
                    type: "button",
                    props: {
                        title: "上传已选",
                        id: "已选Id",
                    },
                    layout: function (make, view) {
                        make.left.equalTo($("上传本地Id").right).offset(10)
                        make.width.equalTo(130)
                    },
                },
                {
                    type: "button",
                    props: {
                        // title: "刷新",
                        id: "freshLocalId",
                        icon: $icon("162"),
                    },
                    layout: function (make, view) {
                        make.left.equalTo($("已选Id").right).offset(10)
                        make.right.equalTo(0)
                        make.height.equalTo($("已选Id").height)
                    },
                }
            ]
        },
        {
            type: "progress",
            props: {
                value: 0,
                id: "progressId"
            },
            layout({ left, top, right, height }) {
                top.equalTo($("view5").bottom).offset(10);
                left.right.inset(10)
                height.equalTo(2)
            },
        },
        {
            type: "view",
            props: {
                id: "view5",
                // bgcolor: $color("red"),
            },
            layout({ top, height }) {
                top.equalTo($("progressId").bottom)
                height.equalTo(34)
                top.left.right.inset(10)
            },
            views: [
                {
                    type: "button",
                    props: {
                        title: "捷径",
                    },
                    layout: function (make, view) {
                        make.width.equalTo(130)
                        make.left.equalTo(0)
                    },
                    events: {
                        tapped: function (_) {
                            $app.openURL("shortcuts://run-shortcut?name=Clipboard%20EX");
                        }
                    }
                }
            ]
        },
    ]
});


let profile = {
    "File": "",
    "Clipboard": "",
    "Type": "Text"
}

async function download() {
    disableButton()
    $("下载spinnerId").loading = true
    httpPara.method = "GET"
    httpPara.body = null
    const resp = await $http.request(httpPara)
    const text = resp.data.Clipboard
    console.info(text)
    $("remote").text = text
    $("下载spinnerId").loading = false
    enableButton()
}

async function upload(text, loaditem) {
    disableButton()
    loaditem.loading = true
    profile.Clipboard = text
    httpPara.body = profile
    httpPara.method = "PUT"
    await $http.request(httpPara)
    loaditem.loading = false
    await download()
    enableButton()
}

$("插入Id").whenTapped(() => {
    $keyboard.insert($("remote").text)
});

$("插入并返回Id").whenTapped(() => {
    $keyboard.insert($("remote").text)
    $keyboard.next()
});

$("freshRemoteId").whenTapped(() => {
    download()
});

$("freshLocalId").whenTapped(() => {
    $("已选textId").text = $keyboard.selectedText ? $keyboard.selectedText : ""
    $("本地labelId").text = $clipboard.text ? $clipboard.text : ""
});

$("上传本地Id").whenTapped(() => {
    upload($("本地labelId").text, $("上传本地spinnerId"))

});

$("已选Id").whenTapped(() => {
    upload($("已选textId").text, $("上传已选spinnerId"))
});

$("已选Id").addEventHandler({
    events: $UIEvent.allEvents,
    handler: sender => {
        $("已选textId").text = $keyboard.selectedText ? $keyboard.selectedText : ""
        $("本地labelId").text = $clipboard.text ? $clipboard.text : ""
    }
});

const buttons = [
    $("freshRemoteId"),
    $("上传本地Id"),
    $("已选Id")
]

function disableButton() {
    buttons.forEach((button, _) => {
        button.userInteractionEnabled = false
        button.bgcolor = $color("#7f7f7f")
    })
}

function enableButton() {
    buttons.forEach((button, _) => {
        button.userInteractionEnabled = true
        button.bgcolor = $("插入Id").bgcolor
    })
}

async function init() {
    $("已选textId").text = $keyboard.selectedText ? $keyboard.selectedText : ""
    $("本地labelId").text = $clipboard.text ? $clipboard.text : ""
    await download()
}
init()