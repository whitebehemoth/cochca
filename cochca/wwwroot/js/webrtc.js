let connection;
let peerConnection;
let localStream;
let session;
let cachedTurnCredentials = null;

async function getTurnCredentials() {
// Return cached credentials if still valid
if (cachedTurnCredentials && cachedTurnCredentials.expiresAt > Date.now() / 1000) {
    return cachedTurnCredentials;
}

// Require active session
if (!session) {
    console.warn('No active session, TURN credentials not available');
    return null;
}

try {
    const response = await fetch(`/api/turn-credentials?sessionId=${encodeURIComponent(session)}`);
    if (!response.ok) {
        if (response.status === 401) {
            console.warn('Session not active, TURN credentials denied');
        } else {
            console.warn('Failed to fetch TURN credentials, using STUN only');
        }
        return null;
    }
        
    cachedTurnCredentials = await response.json();
    console.log('TURN credentials fetched successfully, expires at:', new Date(cachedTurnCredentials.expiresAt * 1000));
    return cachedTurnCredentials;
} catch (error) {
    console.error('Error fetching TURN credentials:', error);
    return null;
    }
}

async function getRtcConfig() {
    const iceServers = [
        { urls: "stun:stun.l.google.com:19302" }
    ];

    const turnCreds = await getTurnCredentials();
    if (turnCreds) {
        iceServers.push({
            urls: turnCreds.urls,
            username: turnCreds.username,
            credential: turnCreds.credential
        });
    }

    return { iceServers };
}

function getElements(required) {
    const localVideo = document.getElementById("localVideo");
    const remoteVideo = document.getElementById("remoteVideo");
    const videoStage = document.getElementById("videoStage");

    if (!localVideo || !remoteVideo || !videoStage) {
        if (required) {
            throw new Error("Video elements not found.");
        }

        return null;
    }

    return { localVideo, remoteVideo, videoStage };
}

function setConnected(isConnected) {
    const elements = getElements(false);
    if (!elements) {
        return;
    }

    const { videoStage, remoteVideo } = elements;

    if (isConnected) {
        videoStage.classList.add("connected");
    } else {
        videoStage.classList.remove("connected");
        remoteVideo.srcObject = null;
    }
}

async function createPeerConnection() {
const elements = getElements(true);
if (!elements) {
    return null;
}

const { remoteVideo } = elements;

const rtcConfig = await getRtcConfig();
peerConnection = new RTCPeerConnection(rtcConfig);

    peerConnection.ontrack = (event) => {
        if (event.streams && event.streams[0]) {
            remoteVideo.srcObject = event.streams[0];
            setConnected(true);
        }
    };

    peerConnection.onicecandidate = (event) => {
        if (event.candidate && connection) {
            connection.invoke("SendIceCandidate", session, JSON.stringify(event.candidate));
        }
    };

    peerConnection.onconnectionstatechange = () => {
        if (peerConnection.connectionState === "disconnected" ||
            peerConnection.connectionState === "failed" ||
            peerConnection.connectionState === "closed") {
            setConnected(false);
        }
    };

    return peerConnection;
}

async function startLocalMedia() {
    const elements = getElements(true);
    if (!elements) {
        return;
    }

    const { localVideo } = elements;

    localStream = await navigator.mediaDevices.getUserMedia({ audio: true, video: true });
    localVideo.srcObject = localStream;

    localStream.getTracks().forEach((track) => {
        peerConnection.addTrack(track, localStream);
    });
}

async function setupSignalR() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/webrtc")
        .withAutomaticReconnect()
        .build();

    connection.on("PeerJoined", async () => {
        if (peerConnection?.signalingState === "stable") {
            const offer = await peerConnection.createOffer();
            await peerConnection.setLocalDescription(offer);
            await connection.invoke("SendOffer", session, JSON.stringify(offer));
        }
    });

    connection.on("ReceiveOffer", async (offerJson) => {
        const offer = JSON.parse(offerJson);
        await peerConnection.setRemoteDescription(offer);
        const answer = await peerConnection.createAnswer();
        await peerConnection.setLocalDescription(answer);
        await connection.invoke("SendAnswer", session, JSON.stringify(answer));
    });

    connection.on("ReceiveAnswer", async (answerJson) => {
        const answer = JSON.parse(answerJson);
        await peerConnection.setRemoteDescription(answer);
    });

    connection.on("ReceiveIceCandidate", async (candidateJson) => {
        const candidate = JSON.parse(candidateJson);
        await peerConnection.addIceCandidate(candidate);
    });

    await connection.start();
    await connection.invoke("JoinSession", session);
}

async function hangupInternal() {
    if (connection) {
        await connection.stop();
        connection = null;
    }

    if (peerConnection) {
        peerConnection.close();
        peerConnection = null;
    }

    if (localStream) {
        localStream.getTracks().forEach((track) => track.stop());
        localStream = null;
    }

    const elements = getElements(false);
    if (elements) {
        const { localVideo, remoteVideo, videoStage } = elements;
        localVideo.srcObject = null;
        remoteVideo.srcObject = null;
        videoStage.classList.remove("connected");
    }
}

window.webrtc = {
    start: async function (sessionId) {
        if (connection || peerConnection) {
            await hangupInternal();
        }

        session = sessionId;
        await createPeerConnection();
        await startLocalMedia();
        await setupSignalR();
    },
    hangup: async function () {
        await hangupInternal();
    },
    toggleVideo: async function (enabled) {
        const elements = getElements(false);
        
        if (enabled) {
            // Enable video: need to get new stream if tracks were stopped
            if (!localStream || localStream.getVideoTracks().length === 0 || 
                localStream.getVideoTracks().every(t => t.readyState === 'ended')) {
                try {
                    // Get new video stream
                    const newStream = await navigator.mediaDevices.getUserMedia({ video: true, audio: true });
                    
                    // Replace old stream
                    if (localStream) {
                        localStream.getTracks().forEach(t => t.stop());
                    }
                    localStream = newStream;
                    
                    if (elements) {
                        elements.localVideo.srcObject = localStream;
                    }
                    
                    // Update peer connection with new tracks
                    if (peerConnection) {
                        const senders = peerConnection.getSenders();
                        const videoTrack = localStream.getVideoTracks()[0];
                        const audioTrack = localStream.getAudioTracks()[0];
                        
                        // Replace video track
                        const videoSender = senders.find(s => s.track && s.track.kind === 'video');
                        if (videoSender && videoTrack) {
                            await videoSender.replaceTrack(videoTrack);
                        } else if (videoTrack) {
                            peerConnection.addTrack(videoTrack, localStream);
                        }
                        
                        // Replace audio track if needed
                        const audioSender = senders.find(s => s.track && s.track.kind === 'audio');
                        if (audioSender && audioTrack) {
                            await audioSender.replaceTrack(audioTrack);
                        } else if (audioTrack) {
                            peerConnection.addTrack(audioTrack, localStream);
                        }
                    }
                } catch (error) {
                    console.error('Failed to enable video:', error);
                    return;
                }
            } else {
                // Just enable existing tracks
                localStream.getVideoTracks().forEach((track) => {
                    track.enabled = true;
                });
            }
            
            if (elements) {
                elements.localVideo.classList.remove("video-off");
            }
        } else {
            // Disable video: completely stop tracks to turn off camera light
            if (localStream) {
                localStream.getVideoTracks().forEach((track) => {
                    track.stop(); // This actually turns off the camera
                });
            }
            
            if (elements) {
                elements.localVideo.classList.add("video-off");
                elements.localVideo.srcObject = null;
            }
        }
    }
};
