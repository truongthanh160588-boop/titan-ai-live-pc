# BÁO CÁO CÂU TRẢ LỜI TỰ ĐỘNG - TITAN AI LIVE PC

Tài liệu này liệt kê toàn bộ câu trả lời tự động hiện có để chỉnh sửa nội dung.

## 1) Script FAQ cố định (khớp theo từ khóa comment)

Nguồn: `App.Wpf/Services/TitanLivestreamScript.cs`

1. `GreetingReply`  
   `Em chào anh. Titan Audio luôn online hỗ trợ kỹ thuật và tư vấn hệ thống âm thanh chuyên nghiệp cho karaoke, sự kiện và sân khấu.`

2. `ComboAudienceReply`  
   `Với số lượng khách như vậy, Titan sẽ tính theo không gian, độ phủ loa và nhu cầu sử dụng thực tế để hệ thống hoạt động ổn định và hiệu quả nhất. Anh vui lòng liên hệ hotline 0974 70 4444 để kỹ thuật Titan tư vấn cấu hình phù hợp.`

3. `EcosystemReply`  
   `Hệ sinh thái Titan được đồng bộ từ loa, main công suất, processor, mixer đến preset xử lý. Khi đồng bộ toàn hệ thống sẽ cho độ ổn định cao, bảo vệ thiết bị tốt hơn, dễ setup và chất âm đồng đều hơn.`

4. `PresetReply`  
   `Titan có preset đồng bộ sẵn theo từng cấu hình loa và main công suất. Khi setup đúng hệ sinh thái, kỹ thuật chỉ cần tinh chỉnh nhẹ theo không gian thực tế là hệ thống đã hoạt động rất hiệu quả.`

5. `T60Reply`  
   `T-60 là processor thế hệ mới của Titan, xử lý ổn định, chống hú hiệu quả, preset dễ dùng và đồng bộ rất tốt với hệ sinh thái Titan. Đây là dòng được nhiều kỹ thuật sử dụng cho karaoke và sân khấu sự kiện.`

6. `SpeakerLineReply`  
   `Titan hướng tới chất âm sạch, lực tốt và hoạt động ổn định lâu dài. Hệ thống được tính toán đồng bộ từ củ loa, thùng loa, phân tần đến preset xử lý để đạt hiệu quả thực tế cao.`

7. `BrandCompareReply`  
   `Mỗi hệ thống có định hướng khác nhau. Titan tập trung vào hiệu quả thực tế, độ ổn định, dễ setup và khả năng đồng bộ toàn hệ thống để phù hợp điều kiện sử dụng tại Việt Nam.`

8. `PowerReply`  
   `Công suất hệ thống Titan được tính theo hiệu quả hoạt động thực tế và độ ổn định lâu dài. Khi phối ghép đúng preset và đúng main công suất, hệ thống hoạt động rất bền và ổn định.`

9. `SetupReply`  
   `Titan hỗ trợ preset, hướng dẫn setup và đồng hành kỹ thuật để người dùng dễ vận hành hơn. Khi đồng bộ đúng hệ sinh thái thì việc setup sẽ nhanh và hiệu quả hơn rất nhiều.`

10. `PriceNeedModelReply`  
    `Anh vui lòng cho Titan xin mã sản phẩm hoặc nhu cầu sử dụng cụ thể để kỹ thuật hỗ trợ báo giá và cấu hình phù hợp nhất.`

11. `PurchaseReply`  
    `Titan hỗ trợ giao hàng toàn quốc, có bảo hành và hỗ trợ kỹ thuật. Anh vui lòng liên hệ hotline 0974 70 4444 để được hỗ trợ nhanh nhất.`

12. `SoundQualityReply`  
    `Titan ưu tiên chất âm sạch, độ phủ đều và khả năng hoạt động ổn định lâu dài. Mỗi cấu hình đều được đồng bộ preset để đạt hiệu quả thực tế tốt nhất.`

---

## 2) Fallback tự động / OCR unclear / kỹ thuật chuyên sâu

Nguồn: `App.Wpf/Services/TitanKnowledgeBase.cs`

1. `BuildNoInfoFallbackReply()`  
```
Tư Vấn Hệ Sinh Thái Titan:
Anh/chị vui lòng liên hệ Hotline Titan Đồng Nai 0967 839 446
hoặc kỹ thuật Titan Đồng Nai qua số 0974 70 4444 gặp Trương Thanh để được tư vấn chi tiết nhé.
```

2. `BuildUnclearOcrReply()`  
```
Tư Vấn Hệ Sinh Thái Titan:
Anh/chị có thể nhắn lại rõ hơn giúp em để Titan hỗ trợ chính xác hơn nhé.
```

3. `BuildTechnicalFallbackReply()`  
```
Tư Vấn Hệ Sinh Thái Titan:
Dạ với các vấn đề kỹ thuật chuyên sâu, setup hệ thống hoặc phối ghép thiết bị, anh/chị vui lòng liên hệ kỹ thuật Titan Đồng Nai: 0974 70 4444 gặp Trương Thanh để được hỗ trợ chi tiết nhé.
```

---

## 3) Mẫu trả lời báo giá tự động từ catalog

Nguồn: `App.Wpf/Services/ProductCatalogService.cs`

1. Trả lời khi khớp 1 sản phẩm rõ ràng (`BuildSinglePriceReply`)  
```
{Tên trợ lý}: {Tên sản phẩm} hiện giá {Giá định dạng}đ / {Đơn vị} anh nhé.
```

2. Trả lời khi mơ hồ nhiều sản phẩm (`BuildAmbiguousPriceReply`)  
```
{Tên trợ lý}: Em thấy anh/chị có thể đang hỏi 1 trong các mẫu sau:
- {SP 1}: {Giá}đ / {Đơn vị}
- {SP 2}: {Giá}đ / {Đơn vị}
...
Anh/chị chốt mã giúp em để báo giá chính xác ngay ạ.
```

---

## 4) Danh sách sản phẩm đang dùng cho auto báo giá

Nguồn: `App.Wpf/products.json`  
(Khi comment chứa mã/alias khớp các dòng dưới, AI sẽ auto báo giá)

- Loa Titan Sub F17 - 24.500.000đ / 1 cái
- Loa Titan Sub SU118-M Italy - 10.000.000đ / 1 cái
- Loa Titan Sub SU118-X CHINA - 11.000.000đ / 1 cái
- Loa Titan Sub SU118-XL Italy - 16.000.000đ / 1 cái
- Loa Titan Sub SU118-ML Italy - 15.000.000đ / 1 cái
- Loa Titan F12-400B Monitor - 6.700.000đ / 1 cái
- Loa Titan F12-400PRO Monitor - 17.000.000đ / 1 cái
- Loa Titan F12-400PLUS Monitor - 11.000.000đ / 1 cái
- Loa Titan F712-V2Lite - 17.500.000đ / 1 cái
- Loa Titan F712-V3 - 25.500.000đ / 1 cái
- Loa Titan VF5 - 30.500.000đ / 1 cái
- Tủ máy 14U khóa bướm nhập - 3.200.000đ / 1 cái
- Thiết bị xử lý số T-60 thế hệ mới - 7.000.000đ / 1 cái
- Nguồn TITAN T-208-USB - 2.900.000đ / 1 cái
- Micro TITAN T-46Pro - 7.800.000đ / 1 bộ
- Mixer TITAN TFX16 - 9.000.000đ / 1 cái
- Tủ đựng micro - 480.000đ / 1 cái
- Main 4 kênh TITAN T-4.16Pro - 21.000.000đ / 1 cái
- Main sub TITAN T-2.18Pro SUB - 17.000.000đ / 1 cái
- Main TITAN T-2.12Pro FULL - 11.800.000đ / 1 cái
- Loa Titan VF4-PRO 2026 - 24.000.000đ / 1 cái
- Loa Titan Sub F15 2026 - 19.500.000đ / 1 cái
- Case VF4 Nằm VF4-PRO - 2.000.000đ / 1 cái
- Main sub TITAN T-2.26Pro SUB - 22.000.000đ / 1 cái

---

## 5) Chỗ cần sửa nếu anh muốn đổi nội dung

- Sửa FAQ cố định: `App.Wpf/Services/TitanLivestreamScript.cs`
- Sửa fallback + hotline + tên kỹ thuật: `App.Wpf/Services/TitanKnowledgeBase.cs`
- Sửa danh sách/mức giá sản phẩm: `App.Wpf/products.json`
- Sửa logic prompt GPT (tone ngắn/dài): `App.Wpf/Services/AiReplyService.cs`

