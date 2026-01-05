const functions = require("firebase-functions");
const { onMessagePublished } = require("firebase-functions/v2/pubsub");
const admin = require("firebase-admin");
admin.initializeApp();

const axios = require("axios");
const FormData = require("form-data");
const Busboy = require("busboy");
const os = require("os");
const path = require("path");
const fs = require("fs");
const AMIVOICE_URL = "https://acp-api-async.amivoice.com/v1/recognitions";

// ★ Google公式ライブラリに変更（google-play-billing-validatorは古くて動作しない）
const { google } = require("googleapis");

// -----------------------------
// Verify Receipt Function
// -----------------------------

exports.verifyReceipt = functions.https.onRequest(async (req, res) => {
    // 1. CORS & Method Check
    res.set("Access-Control-Allow-Origin", "*");
    if (req.method === "OPTIONS") {
        res.set("Access-Control-Allow-Methods", "POST");
        res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
        res.status(204).send("");
        return;
    }
    if (req.method !== "POST") {
        res.status(405).send("Method Not Allowed");
        return;
    }

    // 2. Validate Firebase Auth
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        res.status(401).send("Unauthorized");
        return;
    }
    let uid;
    try {
        const idToken = authHeader.split("Bearer ")[1];
        const decodedToken = await admin.auth().verifyIdToken(idToken);
        uid = decodedToken.uid;
    } catch (error) {
        console.error("Auth Error (verifyReceipt):", error);
        res.status(401).send("Invalid Token");
        return;
    }

    // 3. Parse Body
    // { receipt: "...", platform: "GooglePlay" or "AppStore", productId: "..." }
    const { receipt, platform, productId } = req.body;
    if (!receipt || !productId) {
        res.status(400).send("Missing receipt or productId");
        return;
    }

    console.log(`Verifying receipt for user ${uid}, Platform: ${platform}, Product: ${productId}`);

    try {
        let isValid = false;
        let validationData = {};
        let purchaseToken = null; // ★ 外側スコープで宣言（Firestore保存で使用）
        let purchaseProductId = null;

        if (platform === "GooglePlay") {
            // ★ Firebase Functions v7対応: process.envを使用
            const googleEmail = process.env.IAP_GOOGLE_EMAIL;
            let googleKey = process.env.IAP_GOOGLE_KEY;

            if (!googleEmail || !googleKey) {
                console.error("Missing Google Play Config (IAP_GOOGLE_EMAIL / IAP_GOOGLE_KEY)");
                res.status(500).send("Server Configuration Error: IAP Keys missing");
                return;
            }

            // 秘密鍵のエスケープ処理
            googleKey = googleKey.replace(/\\\\n/g, '\n');
            googleKey = googleKey.replace(/\\n/g, '\n');

            console.log("[DEBUG] Key processed, contains newline:", googleKey.includes('\n'));

            // Unity IAPのレシートをパース
            let packageName;

            try {
                let receiptObj = receipt;
                if (typeof receipt === 'string') {
                    const outer = JSON.parse(receipt);
                    if (outer.Payload) {
                        receiptObj = JSON.parse(outer.Payload);
                    } else {
                        receiptObj = outer;
                    }
                }

                if (receiptObj.json) {
                    const innerJson = (typeof receiptObj.json === 'string') ? JSON.parse(receiptObj.json) : receiptObj.json;
                    packageName = innerJson.packageName;
                    purchaseProductId = innerJson.productId;
                    purchaseToken = innerJson.purchaseToken;
                } else {
                    packageName = receiptObj.packageName || "com.hourglass.Kotonoiro";
                    purchaseProductId = productId;
                    purchaseToken = receiptObj.purchaseToken || receipt;
                }

                console.log("[DEBUG] Parsed:", { packageName, purchaseProductId, tokenLength: purchaseToken?.length });
            } catch (e) {
                console.error("Receipt parsing error:", e.message);
                res.status(400).json({ success: false, message: "Invalid receipt format" });
                return;
            }

            // ★ Google公式API (googleapis) を使用してサブスクリプションを検証
            try {
                const auth = new google.auth.JWT({
                    email: googleEmail,
                    key: googleKey,
                    scopes: ['https://www.googleapis.com/auth/androidpublisher']
                });

                const androidPublisher = google.androidpublisher({ version: 'v3', auth });

                console.log("[DEBUG] Calling Google Play Developer API...");

                const response = await androidPublisher.purchases.subscriptions.get({
                    packageName: packageName,
                    subscriptionId: purchaseProductId,
                    token: purchaseToken
                });

                console.log("[DEBUG] API Response status:", response.status);
                console.log("[DEBUG] Subscription data:", JSON.stringify(response.data));

                // サブスクリプションの状態を確認
                // paymentState: 0=pending, 1=received, 2=free trial, 3=deferred
                // cancelReason: 0=user, 1=system, 2=replaced, 3=developer
                const subData = response.data;

                if (subData.paymentState === 1 || subData.paymentState === 2) {
                    // 支払い済みまたは無料トライアル
                    if (!subData.cancelReason && subData.expiryTimeMillis > Date.now()) {
                        isValid = true;
                        validationData = subData;
                        console.log("Google Play Validation Success: Subscription active");
                    } else if (subData.expiryTimeMillis > Date.now()) {
                        // キャンセル済みだが期限内
                        isValid = true;
                        validationData = subData;
                        console.log("Google Play Validation Success: Cancelled but still active");
                    } else {
                        console.log("Google Play Validation: Subscription expired");
                    }
                } else {
                    console.log("Google Play Validation: Payment not confirmed, state:", subData.paymentState);
                }

            } catch (apiError) {
                console.error("Google Play API Error:", apiError.message);
                if (apiError.response) {
                    console.error("API Error details:", JSON.stringify(apiError.response.data));
                }
                // API エラーでも処理を続行（isValid = false のまま）
            }
        } else if (platform === "AppStore") {
            // Placeholder: Needs Apple Shared Secret
            // For now, if Apple keys aren't set, we might auto-approve OR fail.
            // Given focusing on Android now:
            console.log("Apple verification skipped (Not configured).");
            // Uncomment below when Apple Secret is available
            /*
            const appleSecret = functions.config().iap ? functions.config().iap.apple_secret : null;
            if (appleSecret) {
                // ... validation logic ...
            }
            */
            // Temporary Fake Success for iOS until configured
            isValid = false;
        }

        if (isValid) {
            // 4. Update Firestore Subscription
            // ★ 修正: standardを先にチェック（両方にsubscriptionが含まれる場合があるため）
            let newPlan = "Free";
            if (productId.includes("standard")) {
                newPlan = "Standard";
            } else if (productId.includes("premium")) {
                newPlan = "Premium";
            } else if (productId.includes("ultimate")) {
                newPlan = "Ultimate";
            }

            // ★ RTDNでユーザー特定するためにpurchaseTokenを保存
            await admin.firestore().collection("users").doc(uid).collection("subscription").doc("status").set({
                plan: newPlan,
                updatedAt: admin.firestore.FieldValue.serverTimestamp(),
                lastReceipt: typeof receipt === 'string' ? receipt.substring(0, 50) + "..." : "Object",
                validationMeta: validationData, // Store proof
                downgrade_reason: admin.firestore.FieldValue.delete(), // ★ 購入成功時は解約理由をクリア
                downgrade_pending: admin.firestore.FieldValue.delete(),
                purchaseToken: purchaseToken, // ★ RTDN用: purchaseTokenを保存
                subscriptionId: purchaseProductId // ★ RTDN用: subscriptionIdを保存
            }, { merge: true });

            console.log(`Updated plan to ${newPlan} for user ${uid}`);
            res.status(200).json({ success: true, plan: newPlan });
        } else {
            console.warn(`Invalid receipt for user ${uid}`);
            res.status(400).json({ success: false, message: "Invalid receipt" });
        }

    } catch (error) {
        console.error("Verification Error:", error);
        res.status(500).send("Internal Server Error: " + error.message);
    }
});


// -----------------------------
// Check Subscription Status (解約状態確認用)
// -----------------------------
exports.checkSubscriptionStatus = functions.https.onRequest(async (req, res) => {
    res.set("Access-Control-Allow-Origin", "*");
    if (req.method === "OPTIONS") {
        res.set("Access-Control-Allow-Methods", "POST");
        res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
        res.status(204).send("");
        return;
    }

    // ★ セキュリティ強化: Firebase Auth認証を追加
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        res.status(401).send("Unauthorized: Missing or invalid token");
        return;
    }
    let authenticatedUid;
    try {
        const idToken = authHeader.split("Bearer ")[1];
        const decodedToken = await admin.auth().verifyIdToken(idToken);
        authenticatedUid = decodedToken.uid;
    } catch (error) {
        console.error("Auth Error (checkSubscriptionStatus):", error);
        res.status(401).send("Unauthorized: Invalid token");
        return;
    }

    const { uid, receipt, productId } = req.body;

    // ★ セキュリティ強化: リクエストのuidとトークンのuidを照合
    const targetUid = uid || authenticatedUid;
    if (uid && uid !== authenticatedUid) {
        console.warn(`[checkSubscriptionStatus] UID mismatch: body=${uid}, token=${authenticatedUid}`);
        res.status(403).json({ success: false, message: "Forbidden: UID mismatch" });
        return;
    }

    console.log(`[checkSubscriptionStatus] Checking for user ${targetUid} (authenticated)`);

    try {
        const googleEmail = process.env.IAP_GOOGLE_EMAIL;
        let googleKey = process.env.IAP_GOOGLE_KEY;

        if (!googleEmail || !googleKey) {
            console.error("Missing Google Play Config");
            res.status(500).send("Server Configuration Error");
            return;
        }

        googleKey = googleKey.replace(/\\\\n/g, '\n');
        googleKey = googleKey.replace(/\\n/g, '\n');

        // レシートがある場合はGoogle Playで状態を確認
        if (receipt && productId) {
            let packageName, purchaseToken;

            try {
                let receiptObj = receipt;
                if (typeof receipt === 'string') {
                    const outer = JSON.parse(receipt);
                    if (outer.Payload) {
                        receiptObj = JSON.parse(outer.Payload);
                    } else {
                        receiptObj = outer;
                    }
                }

                if (receiptObj.json) {
                    const innerJson = (typeof receiptObj.json === 'string') ? JSON.parse(receiptObj.json) : receiptObj.json;
                    packageName = innerJson.packageName;
                    purchaseToken = innerJson.purchaseToken;
                } else {
                    packageName = receiptObj.packageName || "com.hourglass.Kotonoiro";
                    purchaseToken = receiptObj.purchaseToken;
                }
            } catch (e) {
                console.error("Receipt parsing error:", e.message);
                res.status(400).json({ success: false, message: "Invalid receipt format" });
                return;
            }

            if (!purchaseToken) {
                console.log("[checkSubscriptionStatus] No purchaseToken, assuming Free");
                // Firestoreを更新
                await updatePlanInFirestore(targetUid, "Free");
                res.status(200).json({ success: true, plan: "Free", expired: true });
                return;
            }

            // Google Play APIで確認
            const auth = new google.auth.JWT({
                email: googleEmail,
                key: googleKey,
                scopes: ['https://www.googleapis.com/auth/androidpublisher']
            });

            const androidPublisher = google.androidpublisher({ version: 'v3', auth });

            try {
                const response = await androidPublisher.purchases.subscriptions.get({
                    packageName: packageName,
                    subscriptionId: productId,
                    token: purchaseToken
                });

                const subData = response.data;
                const expiryTime = parseInt(subData.expiryTimeMillis);
                const now = Date.now();
                const isExpired = expiryTime < now;
                const isCancelled = subData.cancelReason !== undefined;

                console.log(`[checkSubscriptionStatus] expiryTime: ${expiryTime}, now: ${now}, expired: ${isExpired}, cancelled: ${isCancelled}`);
                console.log(`[checkSubscriptionStatus] Human Readable: Expiry=${new Date(expiryTime).toISOString()}, Now=${new Date(now).toISOString()}`);

                if (isExpired) {
                    // 期限切れ → Freeに更新
                    await updatePlanInFirestore(targetUid, "Free");
                    res.status(200).json({ success: true, plan: "Free", expired: true });
                } else {
                    // まだ有効
                    let plan = "Free";
                    if (productId.includes("standard")) plan = "Standard";
                    else if (productId.includes("premium")) plan = "Premium";
                    else if (productId.includes("ultimate")) plan = "Ultimate";

                    res.status(200).json({
                        success: true,
                        plan: plan,
                        expired: false,
                        cancelled: isCancelled,
                        expiryTime: expiryTime
                    });
                }
            } catch (apiError) {
                console.error("Google Play API Error:", apiError.message);
                throw apiError; // Throw to outer catch to return 500
            }
        } else {
            // レシートがない場合 → サブスクリプションは非アクティブとみなす
            // ★ expired: true を返すことで、IAPManager側で「非アクティブ」と判断される
            console.log(`[checkSubscriptionStatus] No receipt provided for user ${targetUid}. Treating as expired.`);
            const doc = await admin.firestore().collection("users").doc(targetUid).collection("subscription").doc("status").get();
            const currentPlan = doc.exists ? (doc.data().plan || "Free") : "Free";
            res.status(200).json({ success: true, plan: currentPlan, expired: true, noReceipt: true });
        }

    } catch (error) {
        console.error("checkSubscriptionStatus Error:", error);
        res.status(500).send("Internal Server Error: " + error.message);
    }
});

// Helper function to update plan in Firestore
async function updatePlanInFirestore(uid, plan) {
    await admin.firestore().collection("users").doc(uid).collection("subscription").doc("status").set({
        plan: plan,
        updatedAt: admin.firestore.FieldValue.serverTimestamp()
    }, { merge: true });
    console.log(`[updatePlanInFirestore] Updated plan to ${plan} for user ${uid}`);
}


// -----------------------------
// AmiVoice Proxy (with Strict Check)
// -----------------------------
exports.proxyAmiVoice = functions.https.onRequest(async (req, res) => {
    // 1. CORS
    res.set("Access-Control-Allow-Origin", "*");
    if (req.method === "OPTIONS") {
        res.set("Access-Control-Allow-Methods", "POST, GET");
        res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
        res.status(204).send("");
        return;
    }

    // 2. Validate Firebase Auth Token
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        res.status(403).send("Unauthorized: Missing or invalid token");
        return;
    }
    let uid;
    try {
        const idToken = authHeader.split("Bearer ")[1];
        const decodedToken = await admin.auth().verifyIdToken(idToken);
        uid = decodedToken.uid;
    } catch (error) {
        console.error("Auth Error:", error);
        res.status(403).send("Unauthorized: Invalid token");
        return;
    }

    // ★★★ 2.5 STRICT PLAN CHECK (Server-Side Enforcement) ★★★
    let currentPlan = "Free";
    let subData = {};
    try {
        const subDoc = await admin.firestore().collection("users").doc(uid).collection("subscription").doc("status").get();
        subData = subDoc.exists ? subDoc.data() : {};
        currentPlan = subData.plan || "Free";

        console.log(`User ${uid} requesting API. Plan: ${currentPlan}`);

        if (currentPlan === "Free") {
            // ★ Freeユーザー: 永久3回（各最大10秒）のAppキー利用をチェック
            const freeTrialCount = subData.free_trial_count || 0;
            const maxFreeTrials = 3;

            if (freeTrialCount >= maxFreeTrials) {
                // 既に3回使い切った
                console.warn(`Blocked API access for Free user ${uid}: Free trial exhausted (${freeTrialCount}/${maxFreeTrials}).`);
                res.status(403).json({
                    error: "FREE_TRIAL_EXHAUSTED",
                    message: "無料お試し枠（3回）を使い切りました。有料プランにアップグレードしてください。",
                    usedCount: freeTrialCount,
                    maxCount: maxFreeTrials
                });
                return;
            }

            // 使用回数をインクリメント
            console.log(`Free user ${uid}: Free trial usage ${freeTrialCount + 1}/${maxFreeTrials}.`);
            await admin.firestore()
                .collection("users").doc(uid)
                .collection("subscription").doc("status")
                .set({
                    free_trial_count: freeTrialCount + 1,
                    updatedAt: admin.firestore.FieldValue.serverTimestamp()
                }, { merge: true });
        }
        // Standard/Premium/Ultimate はそのまま通過
    } catch (dbError) {
        console.error("Plan Check Error:", dbError);
        res.status(500).send("Server Error (Plan Check)");
        return;
    }


    // 3. Get App Key
    const appKey = functions.config().amivoice.appkey;
    if (!appKey) {
        console.error("Config Error: App Key missing");
        res.status(500).send("Server Configuration Error");
        return;
    }

    // 4. Handle Requests
    if (req.method === "POST") {
        // --- POST: Start Recognition ---
        const busboy = Busboy({ headers: req.headers });
        const fields = {};
        const fileWrites = [];
        const tmpFiles = [];

        busboy.on("field", (fieldname, val) => {
            fields[fieldname] = val;
        });

        busboy.on("file", (fieldname, file, { filename }) => {
            const uniqueName = `${Date.now()}-${filename}`;
            const filepath = path.join(os.tmpdir(), uniqueName);
            tmpFiles.push(filepath);

            const writeStream = fs.createWriteStream(filepath);
            file.pipe(writeStream);

            const promise = new Promise((resolve, reject) => {
                file.on("end", () => writeStream.end());
                writeStream.on("finish", resolve);
                writeStream.on("error", reject);
            });
            fileWrites.push(promise);
        });

        busboy.on("finish", async () => {
            try {
                await Promise.all(fileWrites);

                const form = new FormData();
                form.append("u", appKey);

                // ★ UltimateプランはloggingOptOutを強制的にTrueに設定
                let dParam = fields["d"] || "";
                if (currentPlan === "Ultimate") {
                    if (dParam.includes("loggingOptOut=False")) {
                        dParam = dParam.replace(/loggingOptOut=False/g, "loggingOptOut=True");
                    } else if (!dParam.includes("loggingOptOut=True")) {
                        dParam = dParam.trim() + " loggingOptOut=True";
                    }
                    console.log(`Ultimate user ${uid}: loggingOptOut forced to True`);
                }
                form.append("d", dParam);

                if (tmpFiles.length > 0) {
                    form.append("a", fs.createReadStream(tmpFiles[0]), {
                        filename: "audio.wav",
                        contentType: "audio/wav"
                    });
                }

                const response = await axios.post(AMIVOICE_URL, form, {
                    headers: form.getHeaders(),
                    maxContentLength: Infinity,
                    maxBodyLength: Infinity
                });

                tmpFiles.forEach(f => { try { fs.unlinkSync(f); } catch (e) { } });
                res.status(response.status).send(response.data);

            } catch (error) {
                console.error("AmiVoice POST Error:", error.message);
                tmpFiles.forEach(f => { try { fs.unlinkSync(f); } catch (e) { } });
                if (error.response) res.status(error.response.status).send(error.response.data);
                else res.status(500).send(error.message);
            }
        });
        busboy.end(req.rawBody);

    } else if (req.method === "GET") {
        // --- GET: Poll Status ---
        const sessionId = req.query.sessionid;
        if (!sessionId) {
            res.status(400).send("Missing sessionid parameter");
            return;
        }

        try {
            const pollUrl = `${AMIVOICE_URL}/${sessionId}`;
            const response = await axios.get(pollUrl, {
                headers: { "Authorization": `Bearer ${appKey}` }
            });
            res.status(response.status).send(response.data);
        } catch (error) {
            console.error("AmiVoice GET Error:", error.message);
            if (error.response) res.status(error.response.status).send(error.response.data);
            else res.status(500).send(error.message);
        }

    } else {
        res.status(405).send("Method Not Allowed");
    }
});

// -----------------------------
// ★ Plan Management Function (for subscription cancellation)
// Called when client detects subscription is no longer active
// -----------------------------
exports.downgradePlan = functions.https.onRequest(async (req, res) => {
    // 1. CORS
    res.set("Access-Control-Allow-Origin", "*");
    if (req.method === "OPTIONS") {
        res.set("Access-Control-Allow-Methods", "POST");
        res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
        res.status(204).send("");
        return;
    }
    if (req.method !== "POST") {
        res.status(405).send("Method Not Allowed");
        return;
    }

    // 2. Validate Firebase Auth
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        res.status(401).send("Unauthorized");
        return;
    }
    let uid;
    try {
        const idToken = authHeader.split("Bearer ")[1];
        const decodedToken = await admin.auth().verifyIdToken(idToken);
        uid = decodedToken.uid;
    } catch (error) {
        console.error("Auth Error (downgradePlan):", error);
        res.status(401).send("Invalid Token");
        return;
    }

    // 3. Parse Body
    const { newPlan } = req.body;
    if (!newPlan) {
        res.status(400).send("Missing newPlan");
        return;
    }

    // 4. Validate plan value (only allow downgrade to Free for security)
    const allowedPlans = ["Free"];
    if (!allowedPlans.includes(newPlan)) {
        res.status(400).send("Invalid plan. Only downgrade to Free is allowed via this endpoint.");
        return;
    }

    try {
        // 5. Update Firestore (Admin SDK bypasses rules)
        await admin.firestore()
            .collection("users").doc(uid)
            .collection("subscription").doc("status")
            .set({
                plan: newPlan,
                last_updated: admin.firestore.FieldValue.serverTimestamp(),
                downgrade_reason: "subscription_cancelled"
            }, { merge: true });

        console.log(`[downgradePlan] User ${uid} downgraded to ${newPlan}`);
        res.status(200).json({ success: true, plan: newPlan });

    } catch (error) {
        console.error("downgradePlan Error:", error);
        res.status(500).send("Internal Server Error: " + error.message);
    }
});

// -----------------------------
// ★ Quota Management Functions (Security Enhancement)
// Prevents race conditions and offline quota manipulation
// -----------------------------

/**
 * Reserve quota before recording starts.
 * Uses Firestore transaction to prevent race conditions.
 */
exports.reserveQuota = functions.https.onRequest(async (req, res) => {
    // 1. CORS
    res.set("Access-Control-Allow-Origin", "*");
    if (req.method === "OPTIONS") {
        res.set("Access-Control-Allow-Methods", "POST");
        res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
        res.status(204).send("");
        return;
    }
    if (req.method !== "POST") {
        res.status(405).send("Method Not Allowed");
        return;
    }

    // 2. Validate Firebase Auth
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        res.status(401).send("Unauthorized");
        return;
    }
    let uid;
    try {
        const idToken = authHeader.split("Bearer ")[1];
        const decodedToken = await admin.auth().verifyIdToken(idToken);
        uid = decodedToken.uid;
    } catch (error) {
        console.error("Auth Error (reserveQuota):", error);
        res.status(401).send("Invalid Token");
        return;
    }

    // 3. Parse Body
    const { yearMonth, requestedSeconds } = req.body;
    if (!yearMonth || !requestedSeconds) {
        res.status(400).send("Missing yearMonth or requestedSeconds");
        return;
    }

    try {
        const db = admin.firestore();
        const docRef = db.collection("users").doc(uid).collection("subscription").doc("status");

        // Use transaction to ensure atomicity
        const result = await db.runTransaction(async (transaction) => {
            const doc = await transaction.get(docRef);

            // Get plan quota limits
            const plan = doc.exists ? (doc.data().plan || "Free") : "Free";
            const quotaLimits = {
                "Free": 180,
                "Standard": 3600,
                "Premium": 10800,
                "Ultimate": 28800
            };
            const maxQuota = quotaLimits[plan] || 180;

            // Get current usage
            const data = doc.exists ? doc.data() : {};
            const storedYm = data.year_month || "";
            let usedSeconds = (storedYm === yearMonth) ? (data.used_seconds || 0) : 0;
            let reservedSeconds = (storedYm === yearMonth) ? (data.reserved_seconds || 0) : 0;

            // Check if quota available (including reserved)
            const totalUsed = usedSeconds + reservedSeconds;
            const remaining = maxQuota - totalUsed;

            if (remaining <= 0) {
                // No quota available
                return { success: false, remaining: 0, message: "Monthly limit reached" };
            }

            // Calculate how much can be reserved
            const actualReserved = Math.min(requestedSeconds, remaining);

            // Update with reservation
            transaction.set(docRef, {
                year_month: yearMonth,
                used_seconds: usedSeconds,
                reserved_seconds: reservedSeconds + actualReserved,
                last_updated: admin.firestore.FieldValue.serverTimestamp()
            }, { merge: true });

            console.log(`[reserveQuota] User ${uid}: Reserved ${actualReserved}s. Total reserved: ${reservedSeconds + actualReserved}s`);

            return {
                success: true,
                reserved: actualReserved,
                remaining: remaining - actualReserved,
                message: "Quota reserved"
            };
        });

        res.status(200).json(result);

    } catch (error) {
        console.error("reserveQuota Error:", error);
        res.status(500).send("Internal Server Error: " + error.message);
    }
});

/**
 * Confirm quota consumption after recording completes.
 * Converts reserved quota to used quota.
 */
exports.consumeQuota = functions.https.onRequest(async (req, res) => {
    // 1. CORS
    res.set("Access-Control-Allow-Origin", "*");
    if (req.method === "OPTIONS") {
        res.set("Access-Control-Allow-Methods", "POST");
        res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
        res.status(204).send("");
        return;
    }
    if (req.method !== "POST") {
        res.status(405).send("Method Not Allowed");
        return;
    }

    // 2. Validate Firebase Auth
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        res.status(401).send("Unauthorized");
        return;
    }
    let uid;
    try {
        const idToken = authHeader.split("Bearer ")[1];
        const decodedToken = await admin.auth().verifyIdToken(idToken);
        uid = decodedToken.uid;
    } catch (error) {
        console.error("Auth Error (consumeQuota):", error);
        res.status(401).send("Invalid Token");
        return;
    }

    // 3. Parse Body
    const { yearMonth, actualSeconds, releasedSeconds } = req.body;
    if (!yearMonth) {
        res.status(400).send("Missing yearMonth");
        return;
    }

    try {
        const db = admin.firestore();
        const docRef = db.collection("users").doc(uid).collection("subscription").doc("status");

        await db.runTransaction(async (transaction) => {
            const doc = await transaction.get(docRef);
            const data = doc.exists ? doc.data() : {};

            const storedYm = data.year_month || yearMonth;
            let usedSeconds = (storedYm === yearMonth) ? (data.used_seconds || 0) : 0;
            let reservedSeconds = (storedYm === yearMonth) ? (data.reserved_seconds || 0) : 0;

            // Convert reservation to actual usage
            const consumed = actualSeconds || 0;
            const released = releasedSeconds || reservedSeconds; // Release all if not specified

            usedSeconds += consumed;
            reservedSeconds = Math.max(0, reservedSeconds - released);

            transaction.set(docRef, {
                year_month: yearMonth,
                used_seconds: usedSeconds,
                reserved_seconds: reservedSeconds,
                last_updated: admin.firestore.FieldValue.serverTimestamp()
            }, { merge: true });

            console.log(`[consumeQuota] User ${uid}: Consumed ${consumed}s. Total used: ${usedSeconds}s, Reserved: ${reservedSeconds}s`);
        });

        res.status(200).json({ success: true, message: "Quota consumed" });

    } catch (error) {
        console.error("consumeQuota Error:", error);
        res.status(500).send("Internal Server Error: " + error.message);
    }
});

// -----------------------------
// ★ Google Play RTDN Handler (Pub/Sub)
// サブスクリプション状態変更の自動処理
// Firebase Functions v2 API
// -----------------------------

exports.handlePlayNotification = onMessagePublished('play-subscription-notifications', async (event) => {
    const message = event.data.message;
    console.log('[handlePlayNotification] Received notification');

    // 1. メッセージをデコード
    let data;
    try {
        const decoded = Buffer.from(message.data, 'base64').toString('utf-8');
        data = JSON.parse(decoded);
        console.log('[handlePlayNotification] Decoded data:', JSON.stringify(data));
    } catch (e) {
        console.error('[handlePlayNotification] Failed to decode message:', e);
        return;
    }

    // 2. テスト通知の場合
    if (data.testNotification) {
        console.log('[handlePlayNotification] Test notification received. Success!');
        return;
    }

    // 3. サブスクリプション通知かどうか確認
    if (!data.subscriptionNotification) {
        console.log('[handlePlayNotification] Not a subscription notification. Ignoring.');
        return;
    }

    const subNotification = data.subscriptionNotification;
    const packageName = data.packageName;
    const purchaseToken = subNotification.purchaseToken;
    const subscriptionId = subNotification.subscriptionId;
    const notificationType = subNotification.notificationType;

    console.log(`[handlePlayNotification] Type: ${notificationType}, SubscriptionId: ${subscriptionId}`);

    // 通知タイプ定義
    // https://developer.android.com/google/play/billing/subscriptions#real-time-notifications
    // 1: RECOVERED, 2: RENEWED, 3: CANCELED, 4: PURCHASED
    // 5: ON_HOLD, 6: IN_GRACE_PERIOD, 7: RESTARTED
    // 12: REVOKED, 13: EXPIRED
    const CANCEL_TYPES = [3, 12, 13]; // CANCELED, REVOKED, EXPIRED
    const ACTIVE_TYPES = [1, 2, 4, 7]; // RECOVERED, RENEWED, PURCHASED, RESTARTED

    // 4. Google Play API で詳細情報を取得
    let uid = null;
    try {
        const googleEmail = process.env.IAP_GOOGLE_EMAIL;
        let googleKey = process.env.IAP_GOOGLE_KEY;

        if (!googleEmail || !googleKey) {
            console.error('[handlePlayNotification] Missing Google API credentials');
            return;
        }

        googleKey = googleKey.replace(/\\\\n/g, '\n').replace(/\\n/g, '\n');

        const auth = new google.auth.JWT({
            email: googleEmail,
            key: googleKey,
            scopes: ['https://www.googleapis.com/auth/androidpublisher']
        });

        const androidPublisher = google.androidpublisher({ version: 'v3', auth });

        const response = await androidPublisher.purchases.subscriptions.get({
            packageName: packageName,
            subscriptionId: subscriptionId,
            token: purchaseToken
        });

        console.log('[handlePlayNotification] Subscription data:', JSON.stringify(response.data));

        // obfuscatedExternalAccountId でユーザーを特定
        uid = response.data.obfuscatedExternalAccountId;

        if (!uid) {
            console.warn('[handlePlayNotification] No obfuscatedExternalAccountId. Looking up by token...');
            // トークンでFirestoreを検索（フォールバック）
            const usersSnapshot = await admin.firestore()
                .collectionGroup('subscription')
                .where('purchaseToken', '==', purchaseToken)
                .limit(1)
                .get();

            if (!usersSnapshot.empty) {
                uid = usersSnapshot.docs[0].ref.parent.parent.id;
                console.log(`[handlePlayNotification] Found user by token: ${uid}`);
            }
        }

        if (!uid) {
            console.error('[handlePlayNotification] Could not identify user for this notification');
            return;
        }

        console.log(`[handlePlayNotification] User identified: ${uid}`);

        // 5. Firestore を更新
        if (CANCEL_TYPES.includes(notificationType)) {
            // 解約/期限切れ → Free に変更
            const expiryTimeMillis = parseInt(response.data.expiryTimeMillis);
            const now = Date.now();

            if (expiryTimeMillis < now) {
                console.log(`[handlePlayNotification] Subscription expired. Downgrading to Free.`);
                await admin.firestore()
                    .collection('users').doc(uid)
                    .collection('subscription').doc('status')
                    .set({
                        plan: 'Free',
                        updatedAt: admin.firestore.FieldValue.serverTimestamp(),
                        downgrade_reason: `rtdn_type_${notificationType}`,
                        last_notification_type: notificationType
                    }, { merge: true });
            } else {
                console.log(`[handlePlayNotification] Cancelled but not yet expired. Expiry: ${new Date(expiryTimeMillis).toISOString()}`);
                // キャンセル済みだが期限内 → downgrade_pending フラグを設定
                await admin.firestore()
                    .collection('users').doc(uid)
                    .collection('subscription').doc('status')
                    .set({
                        downgrade_pending: true,
                        expiryTimeMillis: expiryTimeMillis,
                        last_notification_type: notificationType,
                        updatedAt: admin.firestore.FieldValue.serverTimestamp()
                    }, { merge: true });
            }
        } else if (ACTIVE_TYPES.includes(notificationType)) {
            // 更新/復帰 → プランを設定
            let newPlan = 'Free';
            if (subscriptionId.includes('standard')) newPlan = 'Standard';
            else if (subscriptionId.includes('premium')) newPlan = 'Premium';
            else if (subscriptionId.includes('ultimate')) newPlan = 'Ultimate';

            console.log(`[handlePlayNotification] Subscription active. Setting plan to: ${newPlan}`);
            await admin.firestore()
                .collection('users').doc(uid)
                .collection('subscription').doc('status')
                .set({
                    plan: newPlan,
                    updatedAt: admin.firestore.FieldValue.serverTimestamp(),
                    downgrade_reason: admin.firestore.FieldValue.delete(),
                    downgrade_pending: admin.firestore.FieldValue.delete(),
                    purchaseToken: purchaseToken,
                    subscriptionId: subscriptionId,
                    last_notification_type: notificationType
                }, { merge: true });
        }

        console.log('[handlePlayNotification] Processing complete.');

    } catch (error) {
        console.error('[handlePlayNotification] Error processing notification:', error);
    }
});

// -----------------------------
// Validate API Key Function
// APIキーの有効性を確認し、特典資格を付与
// -----------------------------

exports.validateApiKey = functions.https.onRequest(async (req, res) => {
    // 1. CORS & Method Check
    res.set("Access-Control-Allow-Origin", "*");
    if (req.method === "OPTIONS") {
        res.set("Access-Control-Allow-Methods", "POST");
        res.set("Access-Control-Allow-Headers", "Content-Type, Authorization");
        res.status(204).send("");
        return;
    }
    if (req.method !== "POST") {
        res.status(405).send("Method Not Allowed");
        return;
    }

    // 2. Validate Firebase Auth
    const authHeader = req.headers.authorization;
    if (!authHeader || !authHeader.startsWith("Bearer ")) {
        res.status(401).send("Unauthorized");
        return;
    }
    let uid;
    try {
        const idToken = authHeader.split("Bearer ")[1];
        const decodedToken = await admin.auth().verifyIdToken(idToken);
        uid = decodedToken.uid;
    } catch (error) {
        console.error("Auth Error (validateApiKey):", error);
        res.status(401).send("Invalid Token");
        return;
    }

    // 3. Parse Body
    const { apiKey } = req.body;
    if (!apiKey) {
        res.status(400).json({ valid: false, error: "Missing apiKey" });
        return;
    }

    console.log(`[validateApiKey] Validating API key for user ${uid}`);

    try {
        // 4. AmiVoice APIに無音テストリクエストを送信
        // 無音（発話なし）の場合は課金されない
        const testUrl = "https://acp-api.amivoice.com/v1/recognize";

        // 無音WAVファイルを生成（PCM 16bit, 16kHz, モノラル, 0.1秒）
        const silentWavBuffer = createSilentWav(0.1);

        const form = new FormData();
        form.append("u", apiKey);
        form.append("d", "grammarFileNames=-a-general");
        form.append("a", silentWavBuffer, {
            filename: "silent.wav",
            contentType: "audio/wav"
        });

        const response = await axios.post(testUrl, form, {
            headers: {
                ...form.getHeaders()
            },
            timeout: 10000,
            validateStatus: () => true // エラーステータスでも例外をスローしない
        });

        console.log(`[validateApiKey] AmiVoice response status: ${response.status}`);

        // 5. レスポンスを判定
        if (response.status === 200) {
            // 認証成功 → APIキー有効
            console.log(`[validateApiKey] API key is valid for user ${uid}`);

            // 特典資格をFirestoreに保存
            const subRef = admin.firestore()
                .collection("users").doc(uid)
                .collection("subscription").doc("status");

            const subDoc = await subRef.get();
            const existingData = subDoc.exists ? subDoc.data() : {};

            // 既に特典を使用済みかチェック
            const offerUsed = existingData.apikey_offer_used || false;

            await subRef.set({
                api_key_valid: true,
                api_key_validated_at: admin.firestore.FieldValue.serverTimestamp(),
                apikey_offer_eligible: !offerUsed, // 未使用なら資格あり
                updatedAt: admin.firestore.FieldValue.serverTimestamp()
            }, { merge: true });

            res.json({
                valid: true,
                offerEligible: !offerUsed,
                offerId: "apikey-registration-trial",
                message: offerUsed
                    ? "APIキーは有効です（特典は使用済み）"
                    : "APIキーは有効です。Standardプラン1カ月無料特典が利用可能です"
            });
        } else if (response.status === 401 || response.status === 403) {
            // 認証失敗 → APIキー無効
            console.log(`[validateApiKey] API key is invalid for user ${uid}`);

            await admin.firestore()
                .collection("users").doc(uid)
                .collection("subscription").doc("status")
                .set({
                    api_key_valid: false,
                    apikey_offer_eligible: false,
                    updatedAt: admin.firestore.FieldValue.serverTimestamp()
                }, { merge: true });

            res.json({
                valid: false,
                offerEligible: false,
                error: "APIキーが無効です。正しいキーを入力してください。"
            });
        } else {
            // その他のエラー
            console.error(`[validateApiKey] Unexpected response: ${response.status}`, response.data);
            res.status(500).json({
                valid: false,
                error: "APIキーの検証中にエラーが発生しました"
            });
        }

    } catch (error) {
        console.error("[validateApiKey] Error:", error.message);
        res.status(500).json({
            valid: false,
            error: "APIキーの検証に失敗しました: " + error.message
        });
    }
});

// 無音WAVファイルを生成するヘルパー関数
function createSilentWav(durationSeconds) {
    const sampleRate = 16000;
    const numChannels = 1;
    const bitsPerSample = 16;
    const numSamples = Math.floor(sampleRate * durationSeconds);
    const dataSize = numSamples * numChannels * (bitsPerSample / 8);
    const fileSize = 44 + dataSize;

    const buffer = Buffer.alloc(fileSize);
    let offset = 0;

    // RIFF header
    buffer.write("RIFF", offset); offset += 4;
    buffer.writeUInt32LE(fileSize - 8, offset); offset += 4;
    buffer.write("WAVE", offset); offset += 4;

    // fmt chunk
    buffer.write("fmt ", offset); offset += 4;
    buffer.writeUInt32LE(16, offset); offset += 4; // chunk size
    buffer.writeUInt16LE(1, offset); offset += 2; // PCM
    buffer.writeUInt16LE(numChannels, offset); offset += 2;
    buffer.writeUInt32LE(sampleRate, offset); offset += 4;
    buffer.writeUInt32LE(sampleRate * numChannels * bitsPerSample / 8, offset); offset += 4;
    buffer.writeUInt16LE(numChannels * bitsPerSample / 8, offset); offset += 2;
    buffer.writeUInt16LE(bitsPerSample, offset); offset += 2;

    // data chunk
    buffer.write("data", offset); offset += 4;
    buffer.writeUInt32LE(dataSize, offset); offset += 4;
    // 残りは0（無音）で初期化済み

    return buffer;
}