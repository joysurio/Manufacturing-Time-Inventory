(function () {
    const photo = document.getElementById('photo');
    const status = document.getElementById('ocrStatus');
    const ocrText = document.getElementById('ocrText');
    const overwrite = document.getElementById('overwrite');
    const sku = document.getElementById('sku');
    const name = document.getElementById('name');
    const applyOcr = document.getElementById('applyOcr');
    const submitBtn = document.getElementById('submitBtn');
    const form = document.getElementById('createWithPhotoForm');
    const startCameraBtn = document.getElementById('startCameraBtn');
    const captureBtn = document.getElementById('captureBtn');
    const stopCameraBtn = document.getElementById('stopCameraBtn');
    const cameraVideo = document.getElementById('cameraVideo');
    const cameraContainer = document.getElementById('cameraContainer');
    const cameraError = document.getElementById('cameraError');
    // Hidden file input with capture="environment" – opens the camera app directly on mobile.
    const cameraCapture = document.getElementById('cameraCapture');

    let ocrInProgress = false;
    let cameraStream = null;
    const captureCanvas = document.createElement('canvas');

    function slugify(text) {
        return text.toString().toUpperCase()
            .normalize('NFD').replace(/\p{Diacritic}/gu, '')
            .replace(/\s+/g, '-')
            .replace(/[^A-Z0-9\-]/g, '')
            .replace(/-+/g, '-')
            .replace(/^-|-$/g, '');
    }

    function firstMeaningfulLine(text) {
        const lines = (text || '')
            .split(/\r?\n/)
            .map(l => (l == null ? '' : l.trim()))
            .filter(l => l != null && l.length > 0);
        return (lines && lines[0]) || '';
    }

    function showCameraError(msg) {
        if (cameraError) {
            cameraError.textContent = msg || '';
            cameraError.classList.toggle('hidden', !msg);
        }
    }

    // Use capture input to open the device camera directly (works on HTTP too).
    function openCameraCapture() {
        if (cameraCapture) {
            showCameraError('');
            cameraCapture.value = '';
            cameraCapture.click();
        }
    }

    // Wire capture input: when the user takes/picks a photo, copy it to the
    // main photo input so OCR runs normally.
    if (cameraCapture) {
        cameraCapture.addEventListener('change', function () {
            var file = cameraCapture.files && cameraCapture.files[0];
            if (!file || !photo) return;
            var dt = new DataTransfer();
            dt.items.add(file);
            photo.files = dt.files;
            photo.dispatchEvent(new Event('change', { bubbles: true }));
        });
    }

    function startCamera() {
        if (cameraStream) return;
        showCameraError('');

        // On mobile / HTTP, getUserMedia is typically blocked.
        // Prefer the capture-input approach which opens the system camera app directly.
        var isSecure = location.protocol === 'https:' || location.hostname === 'localhost' || location.hostname === '127.0.0.1';
        var hasPicker = !!cameraCapture;

        if (!isSecure && hasPicker) {
            openCameraCapture();
            return;
        }

        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            if (hasPicker) {
                openCameraCapture();
            } else {
                showCameraError('Your browser does not support camera access.');
            }
            return;
        }
        var tryVideo = function (constraints) {
            return navigator.mediaDevices.getUserMedia(constraints).then(function (stream) {
                cameraStream = stream;
                if (cameraVideo) cameraVideo.srcObject = stream;
                if (cameraContainer) cameraContainer.classList.remove('hidden');
                if (startCameraBtn) startCameraBtn.classList.add('hidden');
                if (captureBtn) captureBtn.classList.remove('hidden');
                if (stopCameraBtn) stopCameraBtn.classList.remove('hidden');
            });
        };
        tryVideo({ video: { facingMode: 'environment' } })
            .catch(function () { return tryVideo({ video: true }); })
            .catch(function (err) {
                console.error(err);
                // getUserMedia failed (permissions denied or non-HTTPS).
                // Fall back to the capture input which opens the system camera UI.
                if (hasPicker) {
                    openCameraCapture();
                } else {
                    showCameraError('Could not access the camera. Please use "Or choose a file" and select a photo.');
                }
            });
    }

    function stopCamera() {
        if (cameraStream) {
            cameraStream.getTracks().forEach(function (t) { t.stop(); });
            cameraStream = null;
        }
        if (cameraVideo) cameraVideo.srcObject = null;
        if (cameraContainer) cameraContainer.classList.add('hidden');
        if (startCameraBtn) startCameraBtn.classList.remove('hidden');
        if (captureBtn) captureBtn.classList.add('hidden');
        if (stopCameraBtn) stopCameraBtn.classList.add('hidden');
        showCameraError('');
    }

    function captureFromCamera() {
        if (!cameraVideo || !cameraVideo.srcObject || cameraVideo.readyState < 2) {
            if (status) status.textContent = 'Wait for the camera to be ready.';
            return;
        }
        const w = cameraVideo.videoWidth;
        const h = cameraVideo.videoHeight;
        if (!w || !h) return;
        captureCanvas.width = w;
        captureCanvas.height = h;
        const ctx = captureCanvas.getContext('2d');
        ctx.drawImage(cameraVideo, 0, 0);
        captureCanvas.toBlob(function (blob) {
            if (!blob || !photo) return;
            const file = new File([blob], 'capture.jpg', { type: 'image/jpeg' });
            const dt = new DataTransfer();
            dt.items.add(file);
            photo.files = dt.files;
            photo.dispatchEvent(new Event('change', { bubbles: true }));
            stopCamera();
        }, 'image/jpeg', 0.92);
    }

    if (startCameraBtn) startCameraBtn.addEventListener('click', startCamera);
    if (captureBtn) captureBtn.addEventListener('click', captureFromCamera);
    if (stopCameraBtn) stopCameraBtn.addEventListener('click', stopCamera);

    window.addEventListener('beforeunload', stopCamera);
    if (form) form.addEventListener('submit', function () { stopCamera(); });

    function getOcrApiUrl() {
        var base = document.querySelector('script[src*="ocr-item"]');
        if (base && base.src) {
            var u = new URL(base.src);
            return u.origin + (u.pathname.replace(/\/js\/ocr-item\.js.*$/, '') || '') + '/api/ocr';
        }
        return '/api/ocr';
    }

    function applyOcrResult(text, supposedItem) {
        if (ocrText) ocrText.value = text || '';
        if (status) status.textContent = 'OCR completed';
        var nameCandidate = (supposedItem && supposedItem.trim()) ? supposedItem.trim() : (overwrite && overwrite.checked ? firstMeaningfulLine(text || '') : '');
        if (nameCandidate) {
            if (name) name.value = nameCandidate;
            if (overwrite && overwrite.checked && sku && (!sku.value || sku.value.trim() === '')) sku.value = slugify(nameCandidate);
        }
        if (applyOcr && overwrite) applyOcr.checked = overwrite.checked;
    }

    async function runTesseractFallback(file) {
        var imgUrl = URL.createObjectURL(file);
        var worker = Tesseract.createWorker({
            logger: function (m) {
                if (status) status.textContent = m.status === 'recognizing text' ? 'OCR ' + Math.round((m.progress || 0) * 100) + '%' : m.status;
            }
        });
        await worker.load();
        try {
            await worker.loadLanguage('eng');
            await worker.loadLanguage('spa');
            await worker.initialize('eng+spa');
        } catch (e) {
            await worker.loadLanguage('eng');
            await worker.initialize('eng');
        }
        var result = await worker.recognize(imgUrl);
        await worker.terminate();
        URL.revokeObjectURL(imgUrl);
        var text = (result && result.data && result.data.text ? result.data.text : '').trim();
        applyOcrResult(text, null);
        if (!text && status) status.textContent = 'OCR: no text detected';
    }

    if (photo) {
        photo.addEventListener('change', async function () {
            var file = photo.files && photo.files[0];
            if (!file) return;

            ocrInProgress = true;
            if (submitBtn) submitBtn.disabled = true;
            if (status) status.textContent = 'Sending to GPT...';
            if (ocrText) ocrText.value = '';

            try {
                var formData = new FormData();
                formData.append('file', file);
                var resp = await fetch(getOcrApiUrl(), { method: 'POST', body: formData, credentials: 'same-origin' });

                if (resp.ok) {
                    var data = await resp.json();
                    var text = (data && data.text != null) ? String(data.text).trim() : '';
                    var supposedItem = (data && data.supposedItem != null) ? String(data.supposedItem).trim() : null;
                    if (!supposedItem) supposedItem = null;
                    applyOcrResult(text, supposedItem);
                    if (!text && status) status.textContent = 'OCR: no text detected';
                } else if (resp.status === 503 && typeof Tesseract !== 'undefined') {
                    if (status) status.textContent = 'OCR in browser...';
                    await runTesseractFallback(file);
                } else {
                    var errBody = await resp.text();
                    try {
                        var errJson = JSON.parse(errBody);
                        throw new Error(errJson.error || errJson.hint || errBody);
                    } catch (e) {
                        if (e instanceof SyntaxError) throw new Error(errBody);
                        throw e;
                    }
                }
            } catch (err) {
                console.error(err);
                if (typeof Tesseract !== 'undefined') {
                    if (status) status.textContent = 'OCR in browser...';
                    await runTesseractFallback(file);
                } else {
                    if (status) status.textContent = (err && err.message) || 'Error. Configure OpenAI:ApiKey (gpt-4o).';
                }
            } finally {
                ocrInProgress = false;
                if (submitBtn) submitBtn.disabled = false;
            }
        });
    }

    if (form) {
        form.addEventListener('submit', function (e) {
            if (ocrInProgress) {
                e.preventDefault();
                alert('Wait for OCR to finish before saving.');
            }
        });
    }
})();
