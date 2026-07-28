/**
 * Production Freeze Monitor - Client Side Telemetry & Heartbeat (Phase 3)
 * Isolated, zero-impact browser diagnostics script.
 */
(function () {
    'use strict';

    if (window.__FreezeMonitorInitialized) return;
    window.__FreezeMonitorInitialized = true;

    const CONFIG = {
        heartbeatIntervalMs: 15000,
        heartbeatTimeoutMs: 5000,
        heartbeatEndpoint: '/diagnostics/heartbeat',
        browserTelemetryEndpoint: '/diagnostics/browser',
        consecutiveFailureThreshold: 3,
        maxErrorLogHistory: 10
    };

    const state = {
        clientId: 'CLIENT-' + Math.random().toString(36).substring(2, 10) + '-' + Date.now().toString(36),
        unhandledErrorsCount: 0,
        promiseRejectionsCount: 0,
        ajaxFailuresCount: 0,
        consecutiveHeartbeatFailures: 0,
        signalRState: 'Disconnected',
        isOnline: navigator.onLine !== false,
        visibilityState: document.visibilityState || 'visible',
        errorLogHistory: []
    };

    function logLocalError(message) {
        state.errorLogHistory.push(new Date().toISOString() + ': ' + message);
        if (state.errorLogHistory.length > CONFIG.maxErrorLogHistory) {
            state.errorLogHistory.shift();
        }
    }

    function getCsrfToken() {
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        return tokenInput ? tokenInput.value : '';
    }

    // 1. Error & Promise Rejection Handlers
    window.addEventListener('error', function (event) {
        state.unhandledErrorsCount++;
        logLocalError('Error: ' + (event.message || 'Unknown JS Error') + ' at ' + (event.filename || '') + ':' + (event.lineno || 0));
        checkFailureThreshold();
    });

    window.addEventListener('unhandledrejection', function (event) {
        state.promiseRejectionsCount++;
        const reason = event.reason ? (event.reason.message || String(event.reason)) : 'Unhandled Rejection';
        logLocalError('PromiseRejection: ' + reason);
        checkFailureThreshold();
    });

    // 2. Network & Visibility Events
    window.addEventListener('online', function () {
        state.isOnline = true;
    });

    window.addEventListener('offline', function () {
        state.isOnline = false;
        state.ajaxFailuresCount++;
        logLocalError('Network Offline Event');
        checkFailureThreshold();
    });

    document.addEventListener('visibilitychange', function () {
        state.visibilityState = document.visibilityState || 'visible';
    });

    // 3. Intercept Fetch & XMLHttpRequest Failures
    const originalFetch = window.fetch;
    if (originalFetch) {
        window.fetch = function () {
            const args = arguments;
            const url = args[0] && typeof args[0] === 'string' ? args[0] : (args[0] && args[0].url ? args[0].url : '');
            
            // Bypass monitor endpoints from telemetry loop
            if (url && (url.includes(CONFIG.heartbeatEndpoint) || url.includes(CONFIG.browserTelemetryEndpoint))) {
                return originalFetch.apply(this, args);
            }

            return originalFetch.apply(this, args).then(function (response) {
                if (!response.ok && response.status >= 500) {
                    state.ajaxFailuresCount++;
                    logLocalError('Fetch 5xx error: ' + response.status + ' on ' + url);
                    checkFailureThreshold();
                }
                return response;
            }).catch(function (error) {
                state.ajaxFailuresCount++;
                logLocalError('Fetch network error on ' + url);
                checkFailureThreshold();
                throw error;
            });
        };
    }

    const originalOpen = XMLHttpRequest.prototype.open;
    const originalSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (method, url) {
        this._monitorUrl = url;
        return originalOpen.apply(this, arguments);
    };
    XMLHttpRequest.prototype.send = function () {
        const self = this;
        if (self._monitorUrl && !self._monitorUrl.includes(CONFIG.heartbeatEndpoint) && !self._monitorUrl.includes(CONFIG.browserTelemetryEndpoint)) {
            self.addEventListener('load', function () {
                if (self.status >= 500) {
                    state.ajaxFailuresCount++;
                    logLocalError('XHR 5xx error: ' + self.status + ' on ' + self._monitorUrl);
                    checkFailureThreshold();
                }
            });
            self.addEventListener('error', function () {
                state.ajaxFailuresCount++;
                logLocalError('XHR network error on ' + self._monitorUrl);
                checkFailureThreshold();
            });
        }
        return originalSend.apply(this, arguments);
    };

    // 4. Hook into SignalR Lifecycle
    function attachSignalRListeners(connection) {
        if (!connection || typeof connection.onreconnecting !== 'function') return;

        state.signalRState = 'Connected';

        connection.onreconnecting(function () {
            state.signalRState = 'Reconnecting';
            logLocalError('SignalR Reconnecting');
        });

        connection.onreconnected(function () {
            state.signalRState = 'Connected';
            logLocalError('SignalR Reconnected');
        });

        connection.onclose(function () {
            state.signalRState = 'Closed';
            logLocalError('SignalR Connection Closed');
            checkFailureThreshold();
        });
    }

    // Expose hook for application SignalR hub connections
    window.FreezeMonitor = {
        registerSignalRConnection: function (connection) {
            attachSignalRListeners(connection);
        },
        getState: function () {
            return Object.assign({}, state);
        }
    };

    // 5. Periodic Heartbeat Probe
    function sendHeartbeat() {
        const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;
        const timeoutId = controller ? setTimeout(function () { controller.abort(); }, CONFIG.heartbeatTimeoutMs) : null;
        const startTime = Date.now();

        const fetchOptions = {
            method: 'GET',
            headers: { 'Accept': 'application/json' },
            signal: controller ? controller.signal : undefined
        };

        fetch(CONFIG.heartbeatEndpoint, fetchOptions)
            .then(function (response) {
                if (timeoutId) clearTimeout(timeoutId);
                const latency = Date.now() - startTime;
                if (response.ok) {
                    state.consecutiveHeartbeatFailures = 0;
                } else {
                    state.consecutiveHeartbeatFailures++;
                    logLocalError('Heartbeat non-200 status: ' + response.status + ' (' + latency + 'ms)');
                    checkFailureThreshold();
                }
            })
            .catch(function (error) {
                if (timeoutId) clearTimeout(timeoutId);
                state.consecutiveHeartbeatFailures++;
                logLocalError('Heartbeat network failure/timeout: ' + (error.name || error.message));
                checkFailureThreshold();
            });
    }

    // 6. Post Browser Incident Telemetry
    let telemetryInFlight = false;
    function checkFailureThreshold() {
        if (state.consecutiveHeartbeatFailures >= CONFIG.consecutiveFailureThreshold || (state.unhandledErrorsCount + state.ajaxFailuresCount) >= 5) {
            sendBrowserTelemetry();
        }
    }

    function sendBrowserTelemetry() {
        if (telemetryInFlight) return;
        telemetryInFlight = true;

        const perfTiming = window.performance && window.performance.timing ? {
            loadTime: window.performance.timing.loadEventEnd - window.performance.timing.navigationStart,
            domReadyTime: window.performance.timing.domContentLoadedEventEnd - window.performance.timing.navigationStart
        } : null;

        const memoryInfo = window.performance && window.performance.memory ? {
            jsHeapSizeLimit: window.performance.memory.jsHeapSizeLimit,
            totalJSHeapSize: window.performance.memory.totalJSHeapSize,
            usedJSHeapSize: window.performance.memory.usedJSHeapSize
        } : null;

        const payload = {
            timestampUtc: new Date().toISOString(),
            clientId: state.clientId,
            url: window.location.href,
            userAgent: navigator.userAgent,
            unhandledErrorsCount: state.unhandledErrorsCount,
            promiseRejectionsCount: state.promiseRejectionsCount,
            ajaxFailuresCount: state.ajaxFailuresCount,
            consecutiveHeartbeatFailures: state.consecutiveHeartbeatFailures,
            signalRState: state.signalRState,
            isOnline: state.isOnline,
            visibilityState: state.visibilityState,
            performanceTiming: perfTiming,
            memoryInfo: memoryInfo,
            recentErrorLogs: state.errorLogHistory
        };

        const headers = { 'Content-Type': 'application/json' };
        const csrf = getCsrfToken();
        if (csrf) {
            headers['RequestVerificationToken'] = csrf;
        }

        fetch(CONFIG.browserTelemetryEndpoint, {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(payload)
        }).then(function () {
            setTimeout(function () { telemetryInFlight = false; }, 30000); // 30s rate limit
        }).catch(function () {
            telemetryInFlight = false;
        });
    }

    // Start periodic heartbeat cycle
    setInterval(sendHeartbeat, CONFIG.heartbeatIntervalMs);
    // Initial heartbeat after 2s
    setTimeout(sendHeartbeat, 2000);
})();
