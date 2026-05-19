# Những điều cần chú ý để ghi vào README sau này.

## SaveDataType binding
Mục đích chính là để có thể đổi tên class implement ISaveData, ISaveMeta mà vẫn có thể load được save cũ (save trước khi đổi tên).

* Sử dụng attribute [SaveDataType("alias")] với ISaveData, ISaveMeta để đảm bảo sau này đổi tên class thì vẫn dùng được file save cũ. Nếu thay đổi alias đã được khai báo thì save vẫn hỏng bình thường
* [SaveDataType("alias")]. Alias được khai báo phải là unique
* Quá trình scan Assemblies có thể tốn đến vài trăm miliseconds (kiểm tra console để biết chính xác). Nên hãy ưu tiên khởi tại GameSaver từ sớm (Splash, Bootstrap, Loading screen...)
* Assemblies được scan 1 lần khi khởi tạo Serializer. Vậy nên những trường hợp như mod game, load .dll trong runtime cần manual bind Serializer.Binder.Register(type, alias); nhưng đừng lo 99% game thường không gặp trường hợp này.
* Trường hợp BaseSave : ISaveData, PlayerSave : BaseSave thì PlayerSave coi như không có alias. Mỗi class cần khai báo alias riêng. Best practice là class cha không có alias. Đặt alias với những class con.
* ScanAssemblies sẽ skip Interface / abstract class / generic definition

## Không hỗ trợ WebGL, PlayStation 4/5, Xbox One/Series, Nintendo Switch
* WebGL do Async I/O fail, MEMFS không persistent
* Console do dùng SDK riêng của hãng

## Cho phép trùng SaveKey
* Các object có cùng SaveKey sẽ có chung data khi Load và có dữ liệu của 
