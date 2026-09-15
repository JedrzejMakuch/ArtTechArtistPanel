window.arttechSession = {
    read: key => window.sessionStorage.getItem(key),
    write: (key, value) => value === null
        ? window.sessionStorage.removeItem(key)
        : window.sessionStorage.setItem(key, value)
};
