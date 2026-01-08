# サブスクリプションプラン仕様書

> 最終更新: 2026-01-08

## プラン一覧

| プラン | 製品ID | 説明 |
|--------|--------|------|
| Free | - | 無料プラン（デフォルト） |
| Standard | `standard_monthly` | スタンダード（月額） |
| Premium | `premium_monthly` | プレミアム（月額） |
| Ultimate | `ultimate_monthly` | アルティメット（月額・最上位） |

---

## 機能差一覧

### 月間録音クォータ

| プラン | 制限時間 | サーバー強制 |
|--------|----------|:------------:|
| Free | 3分（180秒） | ✅ |
| Standard | 60分（3,600秒） | ✅ |
| Premium | 180分（10,800秒） | ✅ |
| Ultimate | 480分（28,800秒） | ✅ |

### API使用回数（AppKey使用時）

| プラン | 制限 | サーバー強制 |
|--------|------|:------------:|
| Free | **永久で合計3回のみ** | ✅ |
| Standard/Premium/Ultimate | 無制限 | ✅ |

### 自動録音機能

| プラン | 利用可否 | サーバー強制 |
|--------|----------|:------------:|
| Free / Standard | ❌ | ✅ |
| Premium / Ultimate | ✅ | ✅ |

### アーカイブ閲覧期間

| プラン | 閲覧可能期間 | サーバー強制 |
|--------|--------------|:------------:|
| Free | 今月のみ | ✅ |
| Standard | 過去6カ月 | ✅ |
| Premium / Ultimate | 無制限 | ✅ |

### プライバシー設定（loggingOptOut）

| プラン | AmiVoiceログ送信 | サーバー強制 |
|--------|------------------|:------------:|
| Free / Standard / Premium | 許可 | - |
| Ultimate | **拒否（強制）** | ✅ |

---

## 特典

### APIキー登録特典

| 条件 | 特典 |
|------|------|
| Freeユーザーが自身のAPIキーを登録 | Standardプラン1カ月無料 |

---

## 技術実装

### サーバー側（Cloud Functions）

| 関数名 | 役割 |
|--------|------|
| `verifyReceipt` | レシート検証・プラン更新 |
| `checkSubscriptionStatus` | プラン状態確認 |
| `proxyAmiVoice` | API代理（クォータ/自動録音チェック） |
| `getMonthlyDataList` | 閲覧可能月リスト取得 |
| `getMonthlyData` | 月間データ取得（プランチェック付き） |
| `reserveQuota` / `consumeQuota` | クォータ管理 |

### クライアント側

| ファイル | 役割 |
|----------|------|
| `SubscriptionManager.cs` | プラン管理・クォータ計算 |
| `FirestoreManager.cs` | Cloud Functions連携 |
| `ApiHandler.cs` | API呼び出し（isAutoRecord送信） |

### Firestore構造

```
users/{uid}/
  ├── subscription/status    # プラン情報（書き込み禁止）
  ├── monthly_data/{month}   # アートデータ（読み取り禁止→CF経由）
  ├── private_data/settings  # ユーザーAPIキー
  └── history/{autoId}       # APIログ
```

---

## セキュリティ

すべてのプラン制限はサーバー側（Cloud Functions / Firestore Rules）で強制されており、クライアント改変での回避は不可能です。
