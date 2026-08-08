# UMA Task List

Open `UMA > Task List` to manage project work as Unity assets.

Task assets are stored in `Assets/UMA/Tasks`. Each `UMATaskItem` contains:

- a date in `yyyy-MM-dd` format;
- an UMA engineering or art category;
- a title and detailed description;
- a status: New, In Process, Cancelled, or Done;
- a list of Unity object references.

The main window lists tasks by date and title. Status can be changed directly
in the grid. Add Task immediately creates a new asset in the task folder and
opens it in a separate editor. Edit opens the existing asset directly. Changes
are saved automatically; task objects are never added to a scene.
