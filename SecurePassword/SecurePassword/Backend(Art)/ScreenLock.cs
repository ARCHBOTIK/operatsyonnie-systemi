ScreenLockclass ScreenLock {
private:
    bool isLocked;
    std::string password;

public:
    ScreenLock(const std::string& pass) : password(pass), isLocked(false) {}

    void lock() {
        isLocked = true;
    }

    bool unlock(const std::string& input) {
        if (input == password) {
            isLocked = false;
            return true;
        }
        return false;
    }

    bool getStatus() const {
        return isLocked;
    }
};
