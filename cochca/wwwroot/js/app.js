window.app = window.app || {};

window.app.setPath = function (path) {
    if (typeof path !== "string") {
        return;
    }

    history.replaceState(null, "", path);
};
