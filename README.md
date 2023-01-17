# UnitySyncPackage

A Unity package for synchronizing scenes between Unity editors and clients. Can be used in editor at design time and in builded applications.

## Using the Unity package
The package depends on a package from a github repository. Issue: UPM does not support git-based dependencies in packages.

To add the needed github package and this package, find the manifest.json file in the Packages folder of your project and edit it to look like this:

```
{
    "dependencies": {
        "com.dbrizov.naughtyattributes": "https://github.com/dbrizov/NaughtyAttributes.git#upm",
        "com.toph.scene-synchronizer": "https://github.com/ChristophHoltmann/UnitySyncPackage.git#upm",
        ...
    }
}
```
You also can use the UpmGitExtension package: https://github.com/mob-sakai/UpmGitExtension.git to add this package.

## Push Unity package to upm branch
```
git subtree split --prefix=UnitySyncPackage/Assets/SceneSync --branch upm
git tag 1.0.0 upm
git push origin upm --tags
```
For more information: https://www.patreon.com/posts/25070968

To delete a wrong tag:
```
git tag -d tagName
```
If the wrong tag is already pushed:
```
git push origin :tagName
```