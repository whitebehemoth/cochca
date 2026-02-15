(function () {
    if (window.chat) {
        return;
    }

    let connection;
    let sessionId;
    let clientId;
    let dotNetRef;

    async function ensureConnection() {
        if (connection) {
            return;
        }

        connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/chat")
            .withAutomaticReconnect()
            .build();

        connection.on("ReceiveMessage", (senderId, senderName, text) => {
            if (!dotNetRef) {
                return;
            }

            dotNetRef.invokeMethodAsync("ReceiveMessage", {
                senderId,
                senderName,
                text,
                isLocal: senderId === clientId
            });
        });

        connection.on("ReceiveFile", (senderId, senderName, fileName, contentType, base64) => {
            if (!dotNetRef) {
                return;
            }

            dotNetRef.invokeMethodAsync("ReceiveMessage", {
                senderId,
                senderName,
                fileName,
                contentType,
                base64,
                isLocal: senderId === clientId
            });
        });

        await connection.start();
        await connection.invoke("JoinSession", sessionId);
    }

    window.chat = {
        start: async function (session, client, dotnet) {
            sessionId = session;
            clientId = client;
            dotNetRef = dotnet;
            await ensureConnection();
        },
        sendMessage: async function (session, senderId, senderName, text) {
            if (!connection) {
                return;
            }

            await connection.invoke("SendMessage", session, senderId, senderName, text);
        },
        sendFile: async function (session, senderId, senderName, fileName, contentType, base64) {
            if (!connection) {
                return;
            }

            await connection.invoke("SendFile", session, senderId, senderName, fileName, contentType, base64);
        },
        stop: async function () {
            if (connection) {
                await connection.stop();
                connection = null;
            }
            dotNetRef = null;
        }
    };
})();
